using KhypuPyme.Api.Commerce;
using static KhypuPyme.Api.Commerce.CommerceRules;

// Este módulo actualiza piezas y valor por talla exacta de madera y en la ficha general.
// Los cambios se incorporan a la transacción comercial que lo llamó.
namespace KhypuPyme.Api.Timber;

internal sealed class TimberInventory(CommerceTransaction work)
{
    // La clave incluye el negocio, el producto y las tres medidas en unidades internas.
    private async ValueTask<TimberStock?> FindAsync(Product product, TimberDimensions d) =>
        await work.Db.Set<TimberStock>().FindAsync(
            [product.BusinessId, product.Id, d.LengthMicros, d.ThicknessMicros, d.WidthMicros],
            work.Ct);

    public async Task AddAsync(Product product, TimberDimensions? dimension, int count, long value)
    {
        // Una entrada suma piezas y costo al saldo general de la ficha.
        AddStock(product, count, value);
        if (dimension is null) return;

        // Los productos por medida conservan también el saldo de cada tamaño.
        var balance = await FindAsync(product, dimension);
        if (balance is null)
        {
            balance = new TimberStock
            {
                BusinessId = product.BusinessId,
                ProductId = product.Id,
                LengthMicros = dimension.LengthMicros,
                ThicknessMicros = dimension.ThicknessMicros,
                WidthMicros = dimension.WidthMicros,
            };
            // Si esta medida es nueva, se incluye en la misma transacción de la entrada.
            work.Db.Add(balance);
        }
        balance.Stock += count;
        balance.ValueMinor += value;
    }

    public async Task<long> RemoveAsync(Product product, TimberDimensions? dimension, int count)
    {
        // Los artículos sin dimensiones se descuentan del saldo general.
        if (dimension is null) return RemoveStock(product, count);

        // La venta vuelve a leer el saldo de la medida dentro de la transacción.
        var balance = await FindAsync(product, dimension);
        // Una medida que aún no existe equivale a cero piezas disponibles.
        var available = balance?.Stock ?? 0;
        if (balance is null || count > available)
            throw new CommerceFailure($"{product.Name} · {product.Species} · {TimberMeasures.Describe(dimension)}. Pediste {count}, disponibles {available}, faltan {count - available} piezas.", 409);

        // El costo de salida es la parte proporcional del valor actual de esa medida.
        var cost = Share(balance.ValueMinor, 0, count, available);
        // Actualiza primero el saldo específico y después el total de la ficha.
        balance.Stock -= count;
        balance.ValueMinor -= cost;
        product.Stock -= count;
        product.ValueMinor -= cost;
        // La venta usa este costo al guardar el comprobante y calcular su resultado.
        return cost;
    }

    public static TimberDimensions? Check(Product product, TimberDimensions? dimension)
    {
        // Un artículo normal puede venderse sin medidas. Una ficha de madera las exige.
        if (product.Species is null && dimension is null) return null;
        if (product.Species is null || dimension is null)
            throw new CommerceFailure("Selecciona las medidas de la ficha de madera correspondiente.");
        TimberMeasures.Validate(dimension);
        return dimension;
    }

    // Configura especie y tarifas cuando el rubro y la ficha admiten madera por medidas. Una ficha usada conserva su modelo de inventario.
    public static void SetProfile(Product product, TimberPrices? prices, bool canUseTimber, bool isNew)
    {
        if (prices is null && product.Species is null) return;
        if (!canUseTimber || prices is null || (!isNew && product.Species is null))
            throw new CommerceFailure("Crea una ficha nueva para la madera por medidas. Las fichas existentes conservan su modelo.");
        product.Species = Text(prices.Species, 100, "la especie de madera", required: true);
        product.SuperShortMinor = Money(prices.SuperShortMinor, "la tarifa supercorta");
        product.ShortMinor = Money(prices.ShortMinor, "la tarifa corta");
        product.LongMinor = Money(prices.LongMinor, "la tarifa larga");
    }
}
