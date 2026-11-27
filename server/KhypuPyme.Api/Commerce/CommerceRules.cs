// Agrupa las comprobaciones y cálculos comunes. Concentrarlos aquí permite que distintas operaciones interpreten el dinero y el stock del mismo modo.
namespace KhypuPyme.Api.Commerce;

internal sealed class CommerceFailure(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

internal static class CommerceRules
{
    public const long Limit = 1_000_000_000_000;
    public const int QuantityLimit = 1_000_000;
    private static readonly TimeZoneInfo Bolivia = TimeZoneInfo.FindSystemTimeZoneById("America/La_Paz");

    // Convierte la hora al calendario de Bolivia porque un cierre diario o la fecha comercial dependen del día local.
    public static DateOnly LocalDate(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, Bolivia).Date);

    // Valida importes en centavos enteros para que las sumas monetarias mantengan una representación exacta.
    public static long Money(long? value, string label, bool positive = false)
    {
        if (value is null or > Limit || value < (positive ? 1 : 0))
            throw new CommerceFailure($"Indica {label} válido en bolivianos.");
        return value.Value;
    }

    public static long Total(IEnumerable<long> values) => Money(values.Sum(), "un importe total");

    public static string Text(string? input, int length, string label, bool required = false)
    {
        var value = input?.Trim() ?? "";
        if (value.Length > length || value.Any(char.IsControl) || (required && value.Length == 0))
            throw new CommerceFailure($"Revisa {label}. Admite hasta {length} caracteres.");
        return value;
    }

    public static string Payment(string? method, bool allowCredit = false) => method switch
    {
        "cash" or "qr" or "transfer" => method,
        "credit" when allowCredit => method,
        _ => throw new CommerceFailure("Elige una forma de pago válida."),
    };

    // Resta dos importes acumulados para que las porciones sumen el total exacto.
    // Por ejemplo, al repartir 100 centavos entre tres piezas quedan 33, 34 y 33.
    public static long Share(long amount, long start, long end, long total)
    {
        if (total == 0) return 0;
        long At(long index) => (long)Math.Round((decimal)amount * index / total, MidpointRounding.AwayFromZero);
        return At(end) - At(start);
    }

    // Aplica los límites de captura antes de preparar las operaciones y ajusta las comprobaciones al tipo de movimiento.
    public static CommandLine[] Lines(CommandLine[]? lines, bool returning = false, bool buying = false)
    {
        if (lines is not { Length: >= 1 and <= 100 } ||
            lines.Any(line => line is null || line.ProductId == Guid.Empty || line.Quantity is < 1 or > QuantityLimit) ||
            (returning
                ? lines.Select(line => (line.ProductId, line.OriginalLineId)).Distinct().Count()
                : lines.Select(line => (line.ProductId, line.Timber, Lot: buying ? line.Measure?.Lot?.Trim().ToUpperInvariant() : null)).Distinct().Count()) != lines.Length ||
            (!returning && lines.Select(line => (line.ProductId, line.Timber?.LengthMicros, line.Timber?.ThicknessMicros)).Distinct().Count() > 20))
            throw new CommerceFailure("Incluye hasta 20 grupos y 100 medidas distintas con cantidades enteras positivas.");
        return lines;
    }

    // Actualiza cantidad y valor de inventario juntos porque una entrada cambia tanto las existencias como su costo registrado.
    public static void AddStock(Product product, int quantity, long value)
    {
        if (product.Stock + quantity > QuantityLimit)
            throw new CommerceFailure($"Las existencias de {product.Name} superan el límite de piezas.");
        product.Stock += quantity;
        product.ValueMinor = Money(product.ValueMinor + value, "un valor de existencias");
    }

    // Comprueba el saldo y calcula el costo que sale. Ese costo se utiliza después para registrar cuánto valía la mercadería vendida.
    public static long RemoveStock(Product product, int quantity)
    {
        if (quantity > product.Stock)
            throw new CommerceFailure($"{product.Name}: pediste {quantity} piezas y hay {product.Stock} disponibles.", 409);
        var cost = Share(product.ValueMinor, 0, quantity, product.Stock);
        product.Stock -= quantity;
        product.ValueMinor -= cost;
        return cost;
    }
}
