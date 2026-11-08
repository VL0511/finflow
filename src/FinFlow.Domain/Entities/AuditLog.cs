namespace FinFlow.Domain.Entities;

/// <summary>
/// Registro de auditoria: somente inserção. Não há métodos de alteração, nenhum endpoint
/// altera ou remove registros, e o banco bloqueia UPDATE/DELETE na tabela (trigger).
/// Nunca contém senhas, tokens ou hashes.
/// </summary>
public class AuditLog
{
    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Usuário responsável; nulo quando a ação é do sistema (ex.: Worker).</summary>
    public string? UserId { get; private set; }

    /// <summary>E-mail no momento da ação (preservado mesmo se o usuário mudar depois).</summary>
    public string? UserEmail { get; private set; }

    /// <summary>Tipo de operação (ver AuditActions).</summary>
    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string? EntityId { get; private set; }

    /// <summary>Succeeded, Failed, Rejected...</summary>
    public string Result { get; private set; } = string.Empty;

    /// <summary>Detalhes em JSON (contas, valores, moedas, taxa, alterações).</summary>
    public string Details { get; private set; } = "{}";

    private AuditLog() { }

    public AuditLog(
        string action, string entityType, string? entityId, string result, string details,
        string? userId, string? userEmail)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("A ação é obrigatória.");
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("O tipo de entidade é obrigatório.");

        Id = Guid.NewGuid();
        OccurredAt = DateTimeOffset.UtcNow;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Result = string.IsNullOrWhiteSpace(result) ? "Succeeded" : result;
        Details = string.IsNullOrWhiteSpace(details) ? "{}" : details;
        UserId = userId;
        UserEmail = userEmail;
    }
}
