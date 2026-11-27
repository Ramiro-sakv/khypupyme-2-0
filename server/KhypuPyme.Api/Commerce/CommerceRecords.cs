// Describe los datos comerciales que Entity Framework sigue y guarda. CommerceModel define cómo se representan estos tipos en PostgreSQL.
namespace KhypuPyme.Api.Commerce;

// BusinessId identifica a qué negocio pertenece un registro. Las consultas y relaciones comerciales usan este identificador para respetar ese contexto.
internal interface IBusinessRecord { Guid BusinessId { get; } }

// La ficha conserva datos del catálogo y saldos que el servidor actualiza. Sus campos especializados permiten manejar madera, unidades de venta o equipos.
internal sealed class Product : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string NormalizedCode { get; set; } = "";
    public long PriceMinor { get; set; }
    public int Stock { get; set; }
    public long ValueMinor { get; set; }
    public bool Active { get; set; } = true;
    public bool IsService { get; init; }
    public string? Species { get; set; }
    public long? SuperShortMinor { get; set; }
    public long? ShortMinor { get; set; }
    public long? LongMinor { get; set; }
    public string? RetailUnit { get; set; }
    public int BoxSize { get; set; }
    public bool TrackLots { get; set; }
    public decimal Reorder { get; set; }
    public decimal MeasureStock { get; set; }
    public string? Brand { get; set; }
    public string? Presentation { get; set; }
    public string? Tone { get; set; }
    public int? AfterOpeningMonths { get; set; }
    public decimal? RollLength { get; set; }
    public string? DetailJson { get; set; }
    public string? PartsJson { get; set; }
    public string? Reference { get; set; }
    public string? MeasureLabel { get; set; }
    public string? Shelf { get; set; }
    public string? DetailBrand { get; set; }
    public string? DetailModel { get; set; }
    public string? PrintingSize { get; set; }
    public string? PrintingColor { get; set; }
    public string? CountUnit { get; set; }
    public string? MaterialJson { get; set; }
    public Guid? CementSourceId { get; init; }
    public string? Model { get; set; }
    public string? ModelKey { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? VariantKey { get; set; }
    public int? WarrantyMonths { get; set; }
}

internal sealed class Customer : IBusinessRecord
{
    public string? Code { get; init; }
    public Guid BusinessId { get; init; }
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string Phone { get; set; } = "";
}

// La sesión conserva tanto el fondo inicial como el efectivo esperado. Guardar ambas cifras permite explicar la diferencia con los movimientos del historial.
internal sealed class CashSession : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; set; }
    // Minor indica unidades menores de la moneda. En bolivianos, Bs 15.000,00 se guarda como 1500000 centavos.
    public long OpeningMinor { get; init; }
    // Es el efectivo que debería quedar según los movimientos registrados. El dinero realmente contado se registra al cerrar.
    public long ExpectedMinor { get; set; }
    public long? CountedMinor { get; set; }
}

// Es la cabecera de una operación. Reúne su número, fecha, totales y referencias a Caja, cliente u operación de origen cuando corresponden.
internal sealed class Entry : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public Guid Id { get; init; }
    public Guid? CustomerId { get; set; }
    public Guid? CashSessionId { get; set; }
    public Guid? SourceId { get; set; }
    public long Sequence { get; init; }
    public required string Number { get; init; }
    public required string BusinessName { get; init; }
    public string? CustomerName { get; set; }
    public required string Kind { get; init; }
    public required DateOnly Date { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateOnly? ExpiresOn { get; set; }
    public string? TimberUnits { get; set; }
    public bool Practice { get; init; }
    public string? Method { get; set; }
    public string Note { get; set; } = "";
    public long TotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long CashDeltaMinor { get; set; }
    public long SalesMinor { get; set; }
    public long CostMinor { get; set; }
    public long PurchasesMinor { get; set; }
    public long ExpensesMinor { get; set; }
    public long LossesMinor { get; set; }
    public long DebtDeltaMinor { get; set; }
    public List<EntryLine> Lines { get; } = [];
}

// Conserva los datos de una línea tal como se confirmó, incluidos el precio y los detalles de la operación.
internal sealed class EntryLine : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public Guid EntryId { get; init; }
    public Guid LineId { get; init; } = Guid.NewGuid();
    public Guid? OriginalLineId { get; init; }
    public Guid ProductId { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    public int Quantity { get; init; }
    public long UnitMinor { get; init; }
    public long SubtotalMinor { get; init; }
    public long CostMinor { get; init; }
    public bool? Restocked { get; init; }
    public string? Species { get; init; }
    public long? LengthMicros { get; init; }
    public long? ThicknessMicros { get; init; }
    public long? WidthMicros { get; init; }
    public string? DetailsJson { get; init; }
    public decimal? BaseQuantity { get; init; }

    public Timber.TimberDimensions? Timber => LengthMicros is long l && ThicknessMicros is long t && WidthMicros is long w
        ? new(l, t, w) : null;
}

// Mantiene el saldo por cobrar ligado a la operación original. Así un cobro puede actualizar esa deuda concreta.
internal sealed class Receivable : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public Guid SaleId { get; init; }
    public Guid CustomerId { get; init; }
    public long OriginalMinor { get; init; }
    public long BalanceMinor { get; set; }
}

// Conserva el vínculo entre la solicitud y su resultado para que repetir una petición confirmada no duplique la operación.
internal sealed class CommerceReceipt
{
    public Guid AccountId { get; init; }
    public Guid Id { get; init; }
    public Guid BusinessId { get; init; }
    public required string Kind { get; init; }
    public required string Digest { get; init; }
    public Guid EntityId { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}
