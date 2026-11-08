using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlow.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        // UPDATE/DELETE bloqueados por trigger criado na migration (somente inserção).
        builder.ToTable("AuditLogs", t => t.HasTrigger("TR_AuditLogs_AppendOnly"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.OccurredAt).IsRequired();
        // Sem FK para AspNetUsers de propósito: a auditoria sobrevive a qualquer mudança no usuário
        // e guarda o e-mail da época.
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.UserEmail).HasMaxLength(256);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Result).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Details).IsRequired();

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.UserId, a.OccurredAt });
    }
}
