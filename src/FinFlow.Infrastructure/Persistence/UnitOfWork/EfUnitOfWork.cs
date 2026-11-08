using FinFlow.Application.Common;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.UnitOfWork;

public sealed class EfUnitOfWork(FinFlowDbContext context) : IUnitOfWork
{
    private const string ConcurrencyMessage =
        "O registro foi alterado por outro processo. Tente novamente.";

    // 2601: índice UNIQUE; 2627: constraint UNIQUE/PK.
    private static readonly HashSet<int> UniqueViolationErrors = [2601, 2627];

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ConcurrencyMessage, ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new UniqueConstraintViolationException("Valor duplicado em um campo único.", ex);
        }
    }

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        // Com EnableRetryOnFailure, transações explícitas precisam rodar dentro da
        // execution strategy: em falha transitória o bloco inteiro é reexecutado.
        var strategy = context.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async ct =>
            {
                // Descarta estado de tentativas anteriores; a operação recarrega o que precisa.
                context.ChangeTracker.Clear();

                await using var dbTransaction = await context.Database.BeginTransactionAsync(ct);

                await operation(ct);
                await context.SaveChangesAsync(ct);

                await dbTransaction.CommitAsync(ct);
                // Sem Commit (exceção em qualquer ponto), o dispose faz rollback de tudo.
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ConcurrencyMessage, ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new UniqueConstraintViolationException("Valor duplicado em um campo único.", ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => UniqueViolationErrors.Contains(e.Number));
}
