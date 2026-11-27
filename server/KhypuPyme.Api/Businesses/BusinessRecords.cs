// Estas clases representan el negocio, su configuración y la última selección de cada cuenta.
namespace KhypuPyme.Api.Businesses;

// Id identifica el negocio y OwnerId lo enlaza con su cuenta. Una misma cuenta puede administrar varios negocios.
public sealed class Business
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public Guid? PracticeOriginId { get; init; }
    public required string Name { get; init; }
    public required string Industry { get; init; }
    public required string CreationDigest { get; init; }
}

// Separa datos de contacto y preferencias cambiantes de la identidad del negocio.
public sealed class BusinessSettings
{
    public Guid BusinessId { get; init; }
    public Guid OwnerId { get; init; }
    public required string City { get; set; }
    public required string Phone { get; set; }
    public string? TimberUnits { get; set; }
    public int PolicyRevision { get; set; }
    public string DefaultPayment { get; set; } = BusinessPolicy.DefaultPayment;
    public int MaxDiscountPercent { get; set; } = BusinessPolicy.MaxDiscountPercent;
    public int QuoteDays { get; set; } = BusinessPolicy.QuoteDays;
    public bool AllowNewCredit { get; set; } = true;
    public int LotAlertDays { get; set; } = BusinessPolicy.LotAlertDays;

    public OperatingPolicy Policy() => new(PolicyRevision, DefaultPayment, MaxDiscountPercent, QuoteDays, AllowNewCredit, LotAlertDays);
}

// Guarda una selección por dueño para recordar el negocio activo al volver a entrar.
public sealed class AccountSelection
{
    public Guid OwnerId { get; init; }
    public Guid BusinessId { get; set; }
}

public sealed record IndustryOption(string Id, string Name, string Description);
public sealed record BusinessCard(Guid Id, string Name, string Industry);
public sealed record BusinessPage(BusinessCard[] Items, bool HasMore);
public sealed record SelectedBusiness(BusinessCard? Business);
public sealed record BusinessProfile(Guid Id, string Name, string Industry, string City, string Phone, string? TimberUnits, OperatingPolicy Policy);
public sealed record NewBusiness(Guid Id, string? Name, string? Industry, string? City, string? Phone, string? TimberUnits);
public sealed record SettingsInput(Guid BusinessId, string? City, string? Phone, string? TimberUnits);
