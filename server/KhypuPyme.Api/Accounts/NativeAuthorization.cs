using Microsoft.AspNetCore;
using KhypuPyme.Api.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

// El ingreso de la app instalada abre un navegador y devuelve un código al cliente.
// Ese código se intercambia por tokens para que Windows o Android puedan usar la API.
namespace KhypuPyme.Api.Accounts;

internal static class NativeAuthorization
{
    // Valida la ruta a la que se permite regresar después de iniciar sesión en el navegador.
    public static bool IsAuthorizePath(string? path) =>
        path?.StartsWith("/connect/authorize?", StringComparison.Ordinal) == true;

    public static void MapNativeAuthorization(this WebApplication app)
    {
        app.MapGet("/connect/authorize", AuthorizeAsync).ExcludeFromDescription();
        app.MapPost("/connect/token", ExchangeAsync).ExcludeFromDescription();
    }

    // Usa la sesión web y las reglas del cliente registrado para emitir la autorización del dispositivo.
    private static async Task<IResult> AuthorizeAsync(HttpContext context,
        SignInManager<IdentityUser<Guid>> signIn, PilotOptions pilot)
    {
        if (!pilot.Native) return Results.StatusCode(503);

        var user = await signIn.UserManager.GetUserAsync(context.User);
        if (context.Request.Query["prompt"] == "login")
        {
            if (user is not null) await signIn.SignOutAsync();
            var query = QueryString.Create(context.Request.Query.Where(item => item.Key != "prompt"));
            return Results.LocalRedirect("/?returnTo=" + Uri.EscapeDataString(context.Request.Path + query));
        }
        if (user is null)
        {
            var returnTo = context.Request.Path + context.Request.QueryString;
            return Results.LocalRedirect("/?returnTo=" + Uri.EscapeDataString(returnTo));
        }
        if (!user.EmailConfirmed || !pilot.Allows(user.Email) || await signIn.UserManager.IsLockedOutAsync(user))
            return Denied(Errors.AccessDenied);

        var principal = await signIn.CreateUserPrincipalAsync(user);
        principal.SetClaim(Claims.Subject, user.Id.ToString())
            .SetScopes(context.GetOpenIddictServerRequest()!.GetScopes().Intersect([Scopes.OfflineAccess, "khypupyme_api"]))
            .SetResources("khypupyme_api");
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Resuelve el intercambio del código o la renovación de tokens y vuelve a comprobar el acceso de la cuenta.
    private static async Task<IResult> ExchangeAsync(HttpContext context,
        SignInManager<IdentityUser<Guid>> signIn, PilotOptions pilot)
    {
        if (!pilot.Native) return Denied(Errors.InvalidGrant);

        var authenticated = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = authenticated.Principal;
        if (principal is null || await signIn.ValidateSecurityStampAsync(principal) is not { EmailConfirmed: true } user ||
            !pilot.Allows(user.Email) || await signIn.UserManager.IsLockedOutAsync(user))
            return Denied(Errors.InvalidGrant);

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult Denied(string error) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
        }), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    // Registra los identificadores y direcciones de retorno permitidas de los clientes instalados.
    public static async Task RegisterClientsAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var desktop = new Uri("http://127.0.0.1:43823/callback");
        var mobile = new Uri(configuration["NativeAuth:AndroidRedirectUri"] ?? "bo.khypupyme.app:/oauth/callback");

        foreach (var (clientId, name, redirect) in new[]
        {
            ("khypupyme-windows", "KhypuPyme Windows", desktop),
            ("khypupyme-android", "KhypuPyme Android", mobile),
            ("khypupyme2-windows", "KhypuPyme 2.0 Piloto Windows", desktop),
            ("khypupyme2-android", "KhypuPyme 2.0 Piloto Android", new Uri("bo.khypupyme.piloto:/oauth/callback")),
        })
        {
            if (scope.ServiceProvider.GetRequiredService<PilotOptions>().Enabled && !clientId.StartsWith("khypupyme2-", StringComparison.Ordinal))
                continue;
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId, ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Implicit,
                DisplayName = name, RedirectUris = { redirect },
                Permissions =
                {
                    Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + "khypupyme_api",
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            };

            var existing = await manager.FindByClientIdAsync(clientId);
            if (existing is null) await manager.CreateAsync(descriptor);
            else await manager.UpdateAsync(existing, descriptor);
        }
    }
}
