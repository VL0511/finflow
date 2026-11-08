using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Identity;
using FinFlow.Infrastructure.Messaging.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Context;

/// <summary>
/// Contexto único: dados financeiros + Identity (usuários e roles). Um só contexto permite FK
/// real de Accounts.OwnerUserId para AspNetUsers e transações SQL que envolvem os dois lados.
/// O Worker usa o mesmo contexto sem precisar dos serviços web do Identity.
/// </summary>
public sealed class FinFlowDbContext(DbContextOptions<FinFlowDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole, string>(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity primeiro: as configurações do FinFlow referenciam AppUser.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinFlowDbContext).Assembly);
    }
}
