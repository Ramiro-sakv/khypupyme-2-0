using System.ComponentModel.DataAnnotations;
using KhypuPyme.Api.Hosting;
using Microsoft.AspNetCore.Identity;

namespace KhypuPyme.Api.Accounts;

internal static class PilotAccountProvisioning
{
    public static async Task CreateAsync(IServiceProvider services, PilotOptions pilot)
    {
        if (!Console.IsInputRedirected)
            throw new InvalidOperationException("Usa scripts/New-PilotAccount.ps1 para introducir la contraseña sin mostrarla.");

        var email = (await Console.In.ReadLineAsync())?.Trim();
        var password = await Console.In.ReadLineAsync();
        if (email is not { Length: > 0 and <= 256 } || !new EmailAddressAttribute().IsValid(email) || !pilot.IsInvited(email))
            throw new InvalidOperationException("Indica una dirección válida incluida en Pilot__AllowedEmails.");
        if (password is not { Length: >= 12 and <= 128 })
            throw new InvalidOperationException("Usa una contraseña de entre 12 y 128 caracteres.");

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        if (await users.FindByEmailAsync(email) is not null)
            throw new InvalidOperationException("La cuenta del piloto ya existe. Usa su contraseña actual.");
        var created = await users.CreateAsync(new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true
        }, password);
        if (!created.Succeeded) throw new InvalidOperationException("No se pudo crear la cuenta del piloto.");
        Console.WriteLine("Cuenta del piloto creada.");
    }
}
