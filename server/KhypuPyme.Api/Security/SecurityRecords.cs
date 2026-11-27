using System.Text.Json.Serialization;
using KhypuPyme.Api.Commerce;

// Estas clases son los datos de seguridad que quedan guardados después de una solicitud.
namespace KhypuPyme.Api.Security;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProofInput(Guid Id, string Purpose, Guid? BusinessId = null, CommerceCommand? Command = null,
    string? Reason = null, string? Name = null, string? Password = null, string? Code = null, bool SendCode = false);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityConfirmation(Guid Id, string? Name = null, string? Code = null);
public sealed record ProofResult(string Stage, DateTimeOffset ExpiresAt);
public sealed record BusinessDeletionInfo(Guid BusinessId, Guid? SandboxId, string? Name, string State, DateTimeOffset RequestedAt);
public sealed record AuditInfo(Guid OperationId, Guid AccountId, DateTimeOffset At, string Kind, string Reason,
    Guid EntityId, string Effect);
public sealed record SecurityMethods(bool Password, bool Email);

// Conserva la aprobación, su caducidad, su propósito y el estado de uso.
internal sealed class SecurityProof
{
    public Guid OwnerId { get; init; }
    public Guid Id { get; init; }
    public Guid? BusinessId { get; init; }
    public required string Purpose { get; init; }
    public required string Binding { get; init; }
    public required string Reason { get; init; }
    public string Stage { get; set; } = "identity-mail";
    public string? ProtectedCode { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

// Registra el estado que usa el límite de envío de códigos de seguridad.
internal sealed class SecurityMailLimit
{
    public Guid OwnerId { get; init; }
    public DateTimeOffset[] SentAt { get; set; } = [];
}

// Guarda el avance de una baja aunque la limpieza tarde más que una petición.
internal sealed class BusinessDeletion
{
    public Guid BusinessId { get; init; }
    public Guid OwnerId { get; init; }
    public Guid? SandboxId { get; init; }
    public string? Name { get; set; }
    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}

// Registra que el titular solicitó cerrar el acceso a su cuenta.
internal sealed class AccountClosure
{
    public Guid OwnerId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
}

// Vincula el cambio, el titular y el motivo para que se pueda revisar más adelante.
internal sealed class CommerceAudit : IBusinessRecord
{
    public Guid BusinessId { get; init; }
    public Guid OperationId { get; init; }
    public Guid AccountId { get; init; }
    public DateTimeOffset At { get; init; }
    public required string Kind { get; init; }
    public required string Reason { get; init; }
    public Guid EntityId { get; init; }
    public required string Effect { get; init; }
}
