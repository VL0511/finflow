using FinFlow.Domain.Entities;

namespace FinFlow.Application.Common;

public sealed record AuditLogResponse
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? UserId { get; init; }
    public string? UserEmail { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string? EntityId { get; init; }
    public string Result { get; init; } = string.Empty;

    /// <summary>JSON com os detalhes da operação.</summary>
    public string Details { get; init; } = "{}";

    public static AuditLogResponse FromEntity(AuditLog a) => new()
    {
        Id = a.Id,
        OccurredAt = a.OccurredAt,
        UserId = a.UserId,
        UserEmail = a.UserEmail,
        Action = a.Action,
        EntityType = a.EntityType,
        EntityId = a.EntityId,
        Result = a.Result,
        Details = a.Details
    };
}

/// <summary>Filtros de GET /api/admin/audit-logs (combinados com AND).</summary>
public sealed record AuditLogQuery
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? UserId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

/// <summary>Leitura da auditoria. Não existe operação de alteração ou exclusão.</summary>
public interface IAuditLogReader
{
    Task<PagedResult<AuditLogResponse>> ListAsync(AuditLogQuery query, PageRequest page, CancellationToken cancellationToken = default);
}
