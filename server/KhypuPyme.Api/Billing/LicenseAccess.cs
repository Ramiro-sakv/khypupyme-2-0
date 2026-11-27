using KhypuPyme.Api.Commerce;
using KhypuPyme.Api.Persistence;
using Microsoft.EntityFrameworkCore;

// Resuelve el permiso de registrar operaciones según los periodos vigentes de prueba o pago. El servidor consulta este permiso aunque la interfaz tenga un estado anterior.
namespace KhypuPyme.Api.Billing;

internal sealed class LicenseAccess(KhypuPymeDbContext db)
{
    public IQueryable<PaymentAttempt> Payments(Guid owner, Guid business) =>
        db.Set<PaymentAttempt>().Where(x => x.OwnerId == owner && x.BusinessId == business);

    // Comprueba si el instante actual está cubierto. Con la licencia vencida, este control bloquea el comando que lo requiere.
    public async Task RequireAsync(Guid owner, Guid business, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Set<AccountTrial>().AnyAsync(x => x.OwnerId == owner && x.FirstBusinessId == business &&
                x.StartedAt <= now && x.EndsAt > now, ct) ||
            await Payments(owner, business).AnyAsync(x => x.State == "approved" && x.AccessFrom <= now && x.AccessUntil > now, ct))
            return;
        throw new CommerceFailure("El negocio está en modo de consulta. Activa un plan para registrar operaciones.", 403);
    }
}
