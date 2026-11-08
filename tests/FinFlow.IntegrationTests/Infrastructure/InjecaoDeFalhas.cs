using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Persistence.Context;
using FinFlow.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Injeção de falhas/atrasos no início da etapa 2 do ProcessTransactionHandler (bloqueio das
/// contas), DEPOIS de Pending → Processing já comitado. O repositório real continua sendo usado.
/// </summary>
public static class InjecaoDeFalhas
{
    /// <summary>Executa <paramref name="antesDoBloqueio"/> (recebe a conta de origem) antes de bloquear as contas.</summary>
    public static IServiceCollection InterceptarBloqueioDeContas(this IServiceCollection services, Func<string, Task> antesDoBloqueio) =>
        services.AddScoped<IAccountRepository>(sp =>
            new AccountRepositoryInterceptado(new AccountRepository(sp.GetRequiredService<FinFlowDbContext>()), antesDoBloqueio));

    private sealed class AccountRepositoryInterceptado(IAccountRepository inner, Func<string, Task> antesDoBloqueio) : IAccountRepository
    {
        public async Task<(Account? Source, Account? Destination)> GetPairForUpdateAsync(
            string sourceAccountNumber, string destinationAccountNumber, CancellationToken cancellationToken = default)
        {
            await antesDoBloqueio(sourceAccountNumber);
            return await inner.GetPairForUpdateAsync(sourceAccountNumber, destinationAccountNumber, cancellationToken);
        }

        public Task AddAsync(Account account, CancellationToken cancellationToken = default) => inner.AddAsync(account, cancellationToken);
        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);
        public Task<Account?> GetByNumberAsync(string accountNumber, CancellationToken cancellationToken = default) => inner.GetByNumberAsync(accountNumber, cancellationToken);
        public Task<bool> ExistsByNumberAsync(string accountNumber, CancellationToken cancellationToken = default) => inner.ExistsByNumberAsync(accountNumber, cancellationToken);
        public Task<PagedResult<Account>> ListAsync(PageRequest page, AccessScope scope, CancellationToken cancellationToken = default) => inner.ListAsync(page, scope, cancellationToken);
        public Task<IReadOnlyList<AccountCurrencySummary>> SummarizeByCurrencyAsync(AccessScope scope, CancellationToken cancellationToken = default) => inner.SummarizeByCurrencyAsync(scope, cancellationToken);
        public Task<IReadOnlyList<string>> ListNumbersByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default) => inner.ListNumbersByOwnerAsync(ownerUserId, cancellationToken);
    }
}

/// <summary>Quantas falhas ainda serão lançadas para a conta de origem indicada (compartilhado entre escopos).</summary>
public sealed class FalhasSimuladas(string contaDeOrigem, int restantes, Func<Exception> criar)
{
    private int _restantes = restantes;

    public int Restantes => Volatile.Read(ref _restantes);

    public Task TalvezFalharAsync(string sourceAccountNumber)
    {
        if (sourceAccountNumber != contaDeOrigem)
            return Task.CompletedTask;

        // Consome uma falha sem passar de zero (chamadas seguintes seguem normalmente).
        int atual;
        do
        {
            atual = Volatile.Read(ref _restantes);
            if (atual <= 0)
                return Task.CompletedTask;
        }
        while (Interlocked.CompareExchange(ref _restantes, atual - 1, atual) != atual);

        throw criar();
    }
}
