using Microsoft.EntityFrameworkCore;

// Genera códigos cortos para fichas nuevas. La serie es propia de cada negocio y prefijo, así cada comerciante puede buscar por sus códigos.
namespace KhypuPyme.Api.Commerce;

internal sealed class BusinessCodeCounter : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public required string Prefix { get; init; }
    public long Last { get; set; }
}

internal static class BusinessCodes
{
    // El prefijo indica el rubro. Los rubros fuera de esta lista conservan su flujo de alta.
    public static string? ProductPrefix(string industry) => industry switch
    {
        "timber" => "MAD", "convenience" => "TIE", "clothing" => "ROP", "appliances" => "ELE", _ => null,
    };

    // El contador se guarda con el resto de la operación. Si se cancela el alta, el cambio del contador también se revierte.
    public static async Task<string> NextAsync(CommerceTransaction work, string prefix)
    {
        // Los comandos ya mantienen el bloqueo transaccional del negocio.
        var counters = work.Db.Set<BusinessCodeCounter>();
        var counter = await counters.FindAsync([work.Business.Id, prefix], work.Ct)
            ?? counters.Add(new BusinessCodeCounter { BusinessId = work.Business.Id, Prefix = prefix }).Entity;
        return FormattableString.Invariant($"{prefix}-{++counter.Last:D6}");
    }

    // Salta códigos ya ocupados, incluidos los escritos a mano y las variantes agregadas al contexto durante este mismo comando.
    public static async Task<string> NextProductAsync(CommerceTransaction work, string prefix)
    {
        var products = work.Db.Set<Product>();
        string code;
        do
        {
            code = await NextAsync(work, prefix);
        } while (products.Local.Any(x => x.BusinessId == work.Business.Id && x.NormalizedCode == code) ||
                 await products.AnyAsync(x => x.NormalizedCode == code, work.Ct));
        return code;
    }
}
