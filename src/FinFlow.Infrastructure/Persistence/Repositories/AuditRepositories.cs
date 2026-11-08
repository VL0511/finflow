using System.Text.Json;
using FinFlow.Application.Common;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adiciona o registro ao DbContext; ele é gravado no mesmo SaveChanges/commit da operação.
/// O responsável vem do ICurrentUser (claims do servidor), nunca de dados do cliente.
/// </summary>
public sealed class EfAuditTrail(FinFlowDbContext context, ICurrentUser currentUser) : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, string? entityId, object? details = null, string result = AuditResults.Succeeded) =>
        RecordFor(currentUser.UserId, currentUser.Email, action, entityType, entityId, details, result);

    public void RecordFor(string? userId, string? userEmail, string action, string entityType, string? entityId,
        object? details = null, string result = AuditResults.Succeeded) =>
        context.AuditLogs.Add(new AuditLog(
            action,
            entityType,
            entityId,
            result,
            details is null ? "{}" : JsonSerializer.Serialize(details, Json),
            userId,
            userEmail));
}

public sealed class AuditLogReader(FinFlowDbContext context) : IAuditLogReader
{
    public async Task<PagedResult<AuditLogResponse>> ListAsync(AuditLogQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var logs = context.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action)) logs = logs.Where(a => a.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) logs = logs.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) logs = logs.Where(a => a.EntityId == query.EntityId);
        if (!string.IsNullOrWhiteSpace(query.UserId)) logs = logs.Where(a => a.UserId == query.UserId);
        if (query.From is { } from) logs = logs.Where(a => a.OccurredAt >= from);
        if (query.To is { } to) logs = logs.Where(a => a.OccurredAt < to);

        var result = await logs
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .ToPagedResultAsync(page, cancellationToken);

        return result.Map(AuditLogResponse.FromEntity);
    }
}
