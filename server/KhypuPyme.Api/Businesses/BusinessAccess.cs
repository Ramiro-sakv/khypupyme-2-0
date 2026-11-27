using System.Data;
using System.Security.Claims;
using KhypuPyme.Api.Persistence;
using KhypuPyme.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

// Aquí se aplica el contexto de cuenta y negocio en PostgreSQL.
// Las políticas de acceso a filas lo consultan para decidir qué registros puede usar cada solicitud.
namespace KhypuPyme.Api.Businesses;

internal sealed class BusinessAccess(KhypuPymeDbContext db)
{
    public Task<bool> DeletingAsync(Guid business, CancellationToken ct) =>
        db.Set<BusinessDeletion>().AnyAsync(x => x.BusinessId == business, ct);

    public Task<bool> ClosedAsync(Guid owner, CancellationToken ct) =>
        db.Set<AccountClosure>().AnyAsync(x => x.OwnerId == owner, ct);

    public IQueryable<Business> VisibleBusinesses() =>
        db.Businesses.Where(business => !db.Set<BusinessDeletion>().Any(job => job.BusinessId == business.Id));

    // Toma el identificador de la identidad autenticada. Así el cliente no puede elegir a otro dueño enviando su ID en el formulario.
    public static Guid AccountId(HttpContext http) =>
        Guid.Parse(http.User.FindFirstValue("sub") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IDbContextTransaction> OpenAsync(Guid account, Guid? business, CancellationToken ct,
        IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        // Las reglas de acceso a filas necesitan el contexto dentro de la transacción.
        var transaction = await db.Database.BeginTransactionAsync(isolation, ct);
        try
        {
            // Se establece la identidad antes de leer o escribir registros del negocio.
            await SetAsync(account, business, ct);
            return transaction;
        }
        catch
        {
            // Si el contexto no se pudo establecer, cierra la transacción antes de informar el error.
            await transaction.DisposeAsync();
            throw;
        }
    }

    // Serializa cambios de selección de la misma cuenta, aunque tenga abiertas dos pestañas.
    public Task<int> LockSelectionAsync(Guid account, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({account.ToString()}, 2))", ct);

    // Serializa los comandos que afectan al mismo negocio. Una venta en curso puede terminar antes de que la siguiente compruebe el saldo.
    public Task<int> LockBusinessAsync(Guid business, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({business.ToString()}, 3))", ct);

    // El último argumento de set_config hace que los valores duren solo esta transacción.
    public Task<int> SetAsync(Guid account, Guid? business, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.account_id', {account.ToString()}, true), set_config('app.business_id', {business?.ToString() ?? ""}, true)", ct);
}
