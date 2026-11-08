namespace FinFlow.Application.Common;

public interface IUnitOfWork
{
    /// <summary>Persiste todas as alterações pendentes de forma atômica.</summary>
    /// <exception cref="ConcurrencyConflictException">Registro alterado por outro processo.</exception>
    /// <exception cref="UniqueConstraintViolationException">Valor duplicado em índice UNIQUE.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Descarta as alterações pendentes (ex.: para tentar de novo após uma colisão de UNIQUE).</summary>
    void DiscardChanges();

    /// <summary>
    /// Executa <paramref name="operation"/> dentro de uma transação SQL explícita.
    /// Ao final, todas as alterações são salvas e comitadas juntas; qualquer
    /// exceção provoca rollback completo. A operação deve (re)carregar as
    /// entidades que vai alterar, pois pode ser reexecutada em falhas transitórias.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">Registro alterado por outro processo.</exception>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
