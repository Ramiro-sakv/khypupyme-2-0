// Una fila representa el reto vigente de un correo para un propósito, como registro o recuperación.
// Separar los propósitos permite aplicar sus reglas de uso y vencimiento por separado.
namespace KhypuPyme.Api.Accounts;

public sealed class EmailChallenge
{
    public required string Email { get; init; }
    public required string Purpose { get; init; }
    // Guarda el código protegido para que no quede legible directamente en PostgreSQL.
    public string ProtectedCode { get; set; } = "";
    // Pone una fecha límite al canje, aunque el mensaje siga en el correo o el buzón local.
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset[] SentAt { get; set; } = [];
    // Cuenta los intentos para detener el canje después de demasiados códigos incorrectos.
    public int Attempts { get; set; }
    public bool Used { get; set; }
}
