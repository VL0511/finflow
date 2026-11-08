using FinFlow.Application.Common;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Accounts;

public interface IAccountRepository
{
    Task AddAsync(Account account, CancellationToken cancellationToken = default);

    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Account?> GetByNumberAsync(string accountNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Carrega e BLOQUEIA (UPDLOCK) as contas de origem e destino até o fim da transação SQL corrente,
    /// sempre na mesma ordem determinística (número da conta, ordinal) para evitar deadlock entre
    /// transferências cruzadas. Exige uma transação ativa (IUnitOfWork.ExecuteInTransactionAsync).
    /// </summary>
    Task<(Account? Source, Account? Destination)> GetPairForUpdateAsync(
        string sourceAccountNumber, string destinationAccountNumber, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNumberAsync(string accountNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Página de contas ordenada por número da conta (e Id, como desempate determinístico).
    /// Contagem e página são consultadas no banco (COUNT + OFFSET/FETCH).
    /// </summary>
    Task<PagedResult<Account>> ListAsync(PageRequest page, AccessScope scope, CancellationToken cancellationToken = default);

    /// <summary>Quantidade de contas, ativas e soma dos saldos, agrupados por moeda (agregação no banco).</summary>
    Task<IReadOnlyList<AccountCurrencySummary>> SummarizeByCurrencyAsync(AccessScope scope, CancellationToken cancellationToken = default);

    /// <summary>Números das contas de um dono (escopo de visibilidade de transações do Customer).</summary>
    Task<IReadOnlyList<string>> ListNumbersByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);
}
