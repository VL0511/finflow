using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Repositories;

internal static class RowLocking
{
    /// <summary>
    /// UPDLOCK fora de uma transação explícita é liberado ao fim do próprio SELECT e não
    /// protege nada. Falhar alto aqui impede que um uso incorreto passe despercebido.
    /// </summary>
    public static void EnsureActiveTransaction(DbContext context, string operation)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                $"{operation} exige uma transação SQL ativa (use IUnitOfWork.ExecuteInTransactionAsync).");
    }
}
