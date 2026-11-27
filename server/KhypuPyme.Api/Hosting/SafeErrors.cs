using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

// Este manejador transforma errores inesperados en una respuesta que el usuario puede reportar.
namespace KhypuPyme.Api.Hosting;

internal sealed class SafeErrors(ILogger<SafeErrors> logger) : IExceptionHandler
{
    // Entrega una referencia para buscar el incidente en los registros del servidor sin mostrar detalles internos en la pantalla.
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception error, CancellationToken ct)
    {
        var bad = error is BadHttpRequestException or JsonException;
        var id = http.TraceIdentifier;
        logger.LogWarning("Solicitud {Reference} no completada ({ErrorType}).", id, error.GetType().Name);
        await Results.Problem(statusCode: bad ? 400 : 500, detail: bad
            ? "La solicitud no tiene el formato esperado." : "No se pudo completar la solicitud. Referencia " + id + ".",
            extensions: new Dictionary<string, object?> { ["reference"] = id }).ExecuteAsync(http);
        return true;
    }
}
