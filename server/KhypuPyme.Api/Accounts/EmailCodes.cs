using System.Data;
using System.Security.Cryptography;
using System.Text;
using KhypuPyme.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

// Este servicio prepara y canjea códigos de cinco dígitos.
// El estado se guarda en PostgreSQL para controlar reenvíos, intentos y uso aunque se recargue la pantalla.
namespace KhypuPyme.Api.Accounts;

internal sealed class EmailCodes(
    KhypuPymeDbContext db,
    IDataProtectionProvider protection,
    LocalMailbox mailbox,
    TimeProvider clock)
{
    public const string Registration = "registration";
    public const string Recovery = "recovery";
    private readonly IDataProtector protector = protection.CreateProtector("KhypuPyme.EmailCodes.v1");

    // Bloquea operaciones del mismo correo durante la transacción. Así dos peticiones no consumen el mismo código a la vez.
    public async Task<IDbContextTransaction> LockAsync(string email, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({email}, 1))", ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    // Aplica el límite de reenvíos, prepara un código temporal y pide al proveedor de correo que lo entregue cuando corresponde.
    public async Task<IResult> SendAsync(string email, string normalized, string purpose, bool deliver, CancellationToken ct)
    {
        if (!mailbox.IsEnabled)
            return AuthErrors.Fail("El envío de correo todavía requiere configuración.", 503);

        await using var tx = await LockAsync(normalized, ct);
        var now = clock.GetUtcNow();
        var challenge = await db.EmailChallenges.FindAsync([normalized, purpose], ct);
        var recent = challenge?.SentAt.Where(date => date > now.AddHours(-1)).ToArray() ?? [];
        if (recent.Length >= 3 || recent.Any(date => date > now.AddMinutes(-1)))
            return AuthErrors.Fail("Espera antes de solicitar otro código.", 429);

        if (challenge is null)
        {
            challenge = new EmailChallenge { Email = normalized, Purpose = purpose };
            db.EmailChallenges.Add(challenge);
        }

        var previousCode = Unprotect(challenge);
        string code;
        do { code = RandomNumberGenerator.GetInt32(100_000).ToString("D5"); }
        while (code == previousCode);

        challenge.ProtectedCode = protector.Protect(code);
        challenge.ExpiresAt = now.AddMinutes(10);
        challenge.SentAt = [..recent, now];
        challenge.Attempts = 0;
        challenge.Used = false;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        try
        {
            if (deliver)
            {
                await mailbox.DeliverAsync(email, purpose, code, ct);

            }
        }
        catch (IOException)
        {
            return AuthErrors.Fail("No se pudo entregar el código. Intenta más tarde.", 503);
        }
        return Results.NoContent();
    }

    // Comprueba el propósito, vencimiento e intentos antes de aplicar la acción. El estado de uso queda guardado con el cambio.
    public async Task<IResult> RedeemAsync(
        string normalized, string purpose, string code,
        Func<Task<IdentityResult>> apply, CancellationToken ct)
    {
        await using var tx = await LockAsync(normalized, ct);
        var challenge = await db.EmailChallenges.FindAsync([normalized, purpose], ct);
        if (challenge is null || challenge.Used || challenge.ExpiresAt <= clock.GetUtcNow())
            return AuthErrors.Fail("El código venció o ya se usó. Solicita otro.");
        if (challenge.Attempts >= 5)
            return AuthErrors.Fail("Se agotaron los intentos. Solicita otro código.", 429);

        var expected = Unprotect(challenge);
        if (expected is null) return AuthErrors.Fail("El código venció o ya se usó. Solicita otro.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.ASCII.GetBytes(expected)))
        {
            challenge.Attempts++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return challenge.Attempts == 5
                ? AuthErrors.Fail("Se agotaron los intentos. Solicita otro código.", 429)
                : AuthErrors.Fail("El código ingresado es incorrecto.");
        }

        var result = await apply();
        if (!result.Succeeded) return AuthErrors.Identity(result);

        challenge.Used = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    // Recupera el valor protegido para validar el flujo sin escribir el código en los mensajes de diagnóstico.
    private string? Unprotect(EmailChallenge challenge)
    {
        if (challenge.ProtectedCode.Length == 0) return null;
        try { return protector.Unprotect(challenge.ProtectedCode); }
        catch (CryptographicException) { return null; }
    }
}
