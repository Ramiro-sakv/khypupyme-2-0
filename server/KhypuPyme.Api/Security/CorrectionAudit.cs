using System.Text.Json;
using KhypuPyme.Api.Commerce;
using KhypuPyme.Api.Persistence;
using Microsoft.EntityFrameworkCore;

// Las correcciones protegidas conservan quién las autorizó, por qué se hicieron y qué datos relevantes había antes.
namespace KhypuPyme.Api.Security;

internal static class CorrectionAudit
{
    // Clasifica los comandos que requieren acreditar de nuevo la identidad antes de cambiar una operación o ficha.
    public static bool Protected(CommerceCommand command) => command.Kind switch
    {
        "return" or "exchange" or "void-expense" or "void-debt" or "cash-adjust" or "stock-in" or "stock-loss" or "inspection" or "hold-lot" or "release-lot" or
            "discard-lot" or "open-cement" or "configure-business" => true,
        "advance-work" => command.WorkState == "cancelled",
        "product" or "customer" => command.EntityId is not null,
        _ => false,
    };

    // Detecta un cierre con diferencia de efectivo para tratarlo como caso que requiere autorización.
    public static Task<bool> CashDifferenceAsync(KhypuPymeDbContext db, CommerceCommand command, CancellationToken ct) =>
        command.Kind == "close-cash" && command.SourceId is Guid id
            ? db.Set<CashSession>().AnyAsync(x => x.Id == id && x.ClosedAt == null && x.ExpectedMinor != command.AmountMinor, ct)
            : Task.FromResult(false);

    // Obtiene los datos previos que se incluirán en auditoría. Después del cambio sería más difícil saber qué se corrigió.
    public static async Task<object?> BeforeAsync(CommerceTransaction work)
    {
        if (work.Command.Kind == "configure-business")
            return (await work.SettingsAsync()).Policy();
        if (work.Command is { Kind: "product", EntityId: Guid product })
        {
            var item = await work.ProductAsync(product);
            return new { item.Name, item.Code, item.PriceMinor, item.Active, item.SuperShortMinor, item.ShortMinor, item.LongMinor };
        }
        if (work.Command is { Kind: "customer", EntityId: Guid customer })
        {
            var item = await work.CustomerAsync(customer);
            return new { item.Name, item.Phone };
        }
        if (work.Command.Kind == "cash-adjust" && await work.OpenCashAsync() is { } session)
            return new { session.Id, session.ExpectedMinor };
        return null;
    }

    // Agrega el registro de auditoría a la transacción del comando para que se confirme junto con la corrección.
    public static void Record(CommerceTransaction work, Guid account, Guid entity, string reason, object? previous)
    {
        var command = work.Command;
        // La solicitud comercial contiene datos de la operación. Las credenciales viajan por otro contrato.
        var change = JsonSerializer.Serialize(new { Previous = previous, Command = command });
        work.Db.Add(new CommerceAudit
        {
            BusinessId = work.Business.Id, OperationId = command.OperationId, AccountId = account,
            At = work.Now, Kind = command.Kind, Reason = reason, EntityId = entity, Effect = change,
        });
    }
}
