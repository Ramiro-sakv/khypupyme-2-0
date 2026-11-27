using KhypuPyme.Api.Businesses;
using KhypuPyme.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using static KhypuPyme.Api.Commerce.CommerceRules;

namespace KhypuPyme.Api.Commerce;

// Entrega el mismo contexto, negocio y comando a los métodos que colaboran en una operación. Así trabajan sobre las mismas entidades y cambios pendientes.
internal sealed class CommerceTransaction(KhypuPymeDbContext db, Business business,
    CommerceCommand command, DateTimeOffset now, CancellationToken ct)
{
    public KhypuPymeDbContext Db => db;
    public Business Business => business;
    public CommerceCommand Command => command;
    public DateTimeOffset Now => now;
    public DateOnly Today => LocalDate(now);
    public CancellationToken Ct => ct;

    public async Task<BusinessSettings> SettingsAsync() =>
        await db.BusinessSettings.FindAsync([business.Id], ct)
        ?? throw new CommerceFailure("No se encontró la configuración del negocio.", 409);

    public async Task<Product> ProductAsync(Guid id) =>
        await db.Set<Product>().FindAsync([business.Id, id], ct)
        ?? throw new CommerceFailure("El producto no está disponible en este negocio.", 404);

    public async Task<Customer> CustomerAsync(Guid id) =>
        await db.Set<Customer>().FindAsync([business.Id, id], ct)
        ?? throw new CommerceFailure("El cliente no está disponible en este negocio.", 404);

    // Busca la operación de origen y aclara qué tipos se permiten. Por ejemplo, una devolución necesita una venta identificable.
    public async Task<Entry> SourceAsync(string kind, bool includeLines = false, bool allowExchange = false, bool allowManualDebt = false)
    {
        var query = db.Set<Entry>().Where(x => x.Id == command.SourceId &&
            (x.Kind == kind || allowExchange && x.Kind == "exchange" || allowManualDebt && x.Kind == "manual-debt"));
        if (includeLines) query = query.Include(x => x.Lines);
        return await query.SingleOrDefaultAsync(ct)
            ?? throw new CommerceFailure("La operación de origen no está disponible en este negocio.", 404);
    }

    // Prepara una cabecera con su número y fecha comerciales dentro de la transacción actual.
    public async Task<Entry> NewEntryAsync(string prefix)
    {
        var sequence = (await db.Set<Entry>().MaxAsync(x => (long?)x.Sequence, ct) ?? 0) + 1;
        var entry = new Entry
        {
            BusinessId = business.Id, BusinessName = business.Name,
            Id = command.OperationId, Kind = command.Kind, Sequence = sequence,
            Number = $"{prefix}-{Today.Year}-{sequence:D6}", Date = Today, CreatedAt = now,
            Practice = business.PracticeOriginId is not null,
            Note = Text(command.Note, 240, "la referencia"),
            TimberUnits = business.Industry == "timber"
                ? (await SettingsAsync()).TimberUnits
                : null,
        };
        db.Add(entry);
        return entry;
    }

    // Recupera la sesión abierta en el contexto de negocio autorizado. El efectivo se debe registrar sobre esa sesión concreta.
    public Task<CashSession?> OpenCashAsync() => db.Set<CashSession>().SingleOrDefaultAsync(x => x.ClosedAt == null, ct);

    public async Task ApplyCashAsync(Entry entry, long delta)
    {
        // Solo los pagos en efectivo modifican esta Caja.
        if (entry.Method != "cash") return;
        // Una operación en efectivo requiere una sesión abierta en el negocio.
        var session = await OpenCashAsync() ?? throw new CommerceFailure("Abre una caja para registrar efectivo.", 409);
        // delta es positivo para un ingreso y negativo para un egreso. Se mide en centavos.
        var balance = session.ExpectedMinor + delta;
        if (balance < 0)
            throw new CommerceFailure("El saldo de caja no alcanza para este pago en efectivo.", 409);
        session.ExpectedMinor = Money(balance, "un saldo de caja");
        // El comprobante conserva la sesión de Caja y el importe de su movimiento.
        entry.CashSessionId = session.Id;
        entry.CashDeltaMinor = delta;
    }
}
