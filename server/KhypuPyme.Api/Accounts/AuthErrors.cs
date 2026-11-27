using Microsoft.AspNetCore.Identity;

// Estas respuestas convierten errores de cuenta en mensajes que puede mostrar la pantalla.
// El código HTTP permite distinguir un dato inválido de una sesión sin permiso.
namespace KhypuPyme.Api.Accounts;

internal static class AuthErrors
{
    // Empaqueta el mensaje y su estado HTTP en el formato que lee el cliente.
    public static IResult Fail(string message, int status = 400) =>
        Results.Problem(statusCode: status, detail: message);

    // Adapta los errores de ASP.NET Identity a las respuestas públicas de esta API.
    public static IResult Identity(IdentityResult result)
    {
        var error = result.Errors.First().Code;
        var message = error switch
        {
            "PasswordTooShort" => "Usa al menos 12 caracteres para la contraseña.",
            "DuplicateEmail" or "DuplicateUserName" => "Este correo ya está registrado. Ingresa o recupera tu contraseña.",
            _ => "No se pudo completar la solicitud. Revisa los datos ingresados.",
        };
        return Fail(message);
    }
}
