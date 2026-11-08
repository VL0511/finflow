using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Users;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Exceptions;
using FluentValidation;

namespace FinFlow.Application.Accounts;

public interface IAccountService
{
    Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<AccountResponse>> ListAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task<AccountSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<AccountResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AccountBalanceResponse> GetBalanceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Conta pelo ID, já conferindo o escopo do usuário (fora do escopo = 404).</summary>
    Task<Account> GetAccessibleAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class AccountService(
    IAccountRepository repository,
    IUnitOfWork unitOfWork,
    IValidator<CreateAccountRequest> validator,
    IAccountNumberGenerator numberGenerator,
    ICurrencyService currencies,
    IUserDirectory users,
    ICurrentUser currentUser,
    IAuditTrail audit) : IAccountService
{
    /// <summary>Tentativas de gerar um número livre (colisões são raríssimas: ~900 milhões de números).</summary>
    public const int MaxNumberAttempts = 5;

    public async Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var currency = await currencies.GetActiveOrThrowAsync(request.Currency, cancellationToken);
        if (!currency.HasValidScale(request.InitialBalance))
            throw new DomainException($"O saldo em {currency.Code} aceita no máximo {currency.DecimalPlaces} casa(s) decimal(is).");

        var ownerUserId = await ResolveOwnerAsync(request, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            var number = numberGenerator.Next();
            if (await repository.ExistsByNumberAsync(number, cancellationToken))
                continue; // checagem rápida; a garantia real é o índice UNIQUE abaixo

            var account = new Account(number, request.HolderName, request.InitialBalance, currency.Code, request.IsActive, ownerUserId);
            await repository.AddAsync(account, cancellationToken);
            audit.Record(AuditActions.AccountCreated, nameof(Account), account.Id.ToString(), new
            {
                account.AccountNumber,
                account.HolderName,
                account.Currency,
                InitialBalance = account.Balance,
                account.IsActive,
                account.OwnerUserId
            });

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return AccountResponse.FromEntity(account);
            }
            catch (UniqueConstraintViolationException) when (attempt < MaxNumberAttempts)
            {
                // Outro cadastro simultâneo gravou o mesmo número entre a checagem e o INSERT.
                unitOfWork.DiscardChanges();
            }
        }
    }

    /// <summary>Customer: sempre ele mesmo. Administrator: o informado (deve existir e estar ativo) ou nenhum.</summary>
    private async Task<string?> ResolveOwnerAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return string.IsNullOrWhiteSpace(request.OwnerUserId) ? null : request.OwnerUserId; // sistema/seed

        if (!currentUser.IsInRole(Roles.Administrator))
            return currentUser.UserId ?? throw new ForbiddenException("Usuário sem identificação.");

        if (string.IsNullOrWhiteSpace(request.OwnerUserId))
            return null;

        if (!await users.IsActiveUserAsync(request.OwnerUserId, cancellationToken))
            throw new DomainException("O dono informado não existe ou está desativado.");
        return request.OwnerUserId;
    }

    public async Task<PagedResult<AccountResponse>> ListAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var accounts = await repository.ListAsync(page, AccessScope.For(currentUser), cancellationToken);
        return accounts.Map(AccountResponse.FromEntity);
    }

    public async Task<AccountSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var balances = await repository.SummarizeByCurrencyAsync(AccessScope.For(currentUser), cancellationToken);
        var total = balances.Sum(b => b.Accounts);
        var active = balances.Sum(b => b.ActiveAccounts);

        return new AccountSummaryResponse
        {
            TotalAccounts = total,
            ActiveAccounts = active,
            InactiveAccounts = total - active,
            Balances = balances.OrderBy(b => b.Currency, StringComparer.Ordinal).ToList()
        };
    }

    public async Task<AccountResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        AccountResponse.FromEntity(await GetAccessibleAsync(id, cancellationToken));

    public async Task<AccountBalanceResponse> GetBalanceAsync(Guid id, CancellationToken cancellationToken = default) =>
        AccountBalanceResponse.FromEntity(await GetAccessibleAsync(id, cancellationToken));

    public async Task<Account> GetAccessibleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await repository.GetByIdAsync(id, cancellationToken);
        // Fora do escopo responde igual a inexistente: não revela que a conta existe.
        if (account is null || !AccessScope.For(currentUser).CanAccess(account))
            throw new NotFoundException($"Conta '{id}' não encontrada.");
        return account;
    }
}
