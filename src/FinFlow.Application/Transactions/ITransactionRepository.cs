using FinFlow.Application.Common;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Transactions;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Carrega e BLOQUEIA (UPDLOCK) a transação até o fim da transação SQL corrente.
    /// Serializa processamentos concorrentes da mesma transação (mensagem duplicada,
    /// várias instâncias do Worker, recuperação). Exige uma transação ativa.
    /// </summary>
    Task<Transaction?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// IDs das transações paradas em Processing desde antes de <paramref name="olderThan"/>
    /// (ex.: mensagem enviada à DLQ depois da etapa 1), das mais antigas para as mais novas.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListStaleProcessingAsync(
        DateTimeOffset olderThan, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// IDs das transações em Pending criadas antes de <paramref name="olderThan"/> (mensagem perdida
    /// antes da etapa 1), das mais antigas para as mais novas. É só uma lista de candidatas: o status
    /// pode mudar depois da consulta, e quem decide é o ProcessTransactionHandler (rowversion + UPDLOCK).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListStalePendingAsync(
        DateTimeOffset olderThan, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Página de transações que atendem ao filtro, da mais recente para a mais antiga
    /// (CreatedAt desc, Id desc como desempate). Contagem e página usam a mesma consulta filtrada.
    /// </summary>
    Task<PagedResult<Transaction>> ListAsync(
        TransactionListFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quantidade de transações por status (agregação no banco). Status sem registros não aparecem.
    /// <paramref name="visibleAccounts"/>: escopo do usuário (nulo = todas).
    /// </summary>
    Task<IReadOnlyDictionary<TransactionStatus, int>> CountByStatusAsync(
        IReadOnlyCollection<string>? visibleAccounts = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entradas/saídas por dia das transferências COMPLETED da conta, liquidadas em
    /// [<paramref name="from"/>, <paramref name="toExclusive"/>). O dia é o de UpdatedAt
    /// (momento da liquidação) deslocado por <paramref name="utcOffsetMinutes"/>.
    /// </summary>
    Task<IReadOnlyList<DailyCashFlow>> GetDailyCashFlowAsync(
        string accountNumber,
        DateTimeOffset from,
        DateTimeOffset toExclusive,
        int utcOffsetMinutes,
        CancellationToken cancellationToken = default);
}
