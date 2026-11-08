using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Messaging;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Exceptions;
using FinFlow.Domain.Services;
using FluentValidation;
using FluentValidation.Results;

namespace FinFlow.Application.Transactions;

public interface ITransactionService
{
    Task<TransactionResponse> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default);
    Task<TransferPreviewResponse> PreviewAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<TransactionResponse>> ListAsync(
        TransactionListQuery query, PageRequest page, CancellationToken cancellationToken = default);
    Task<TransactionSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<AccountCashFlowResponse> GetAccountCashFlowAsync(
        Guid accountId, AccountCashFlowQuery query, CancellationToken cancellationToken = default);
    Task<TransactionResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TransactionStatusResponse> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TransactionResponse> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class TransactionService(
    ITransactionRepository transactions,
    IAccountRepository accounts,
    IMessageOutbox outbox,
    IUnitOfWork unitOfWork,
    IValidator<CreateTransactionRequest> validator,
    IExchangeRateService exchangeRates,
    ICurrentUser currentUser,
    IAuditTrail audit) : ITransactionService
{
    /// <summary>
    /// Registra a transação como Pending e enfileira o evento TransactionRequested
    /// no outbox. Transação, evento, snapshot do câmbio e auditoria são gravados no mesmo
    /// SaveChanges (atômico); a publicação no RabbitMQ acontece depois, em background. A
    /// movimentação de saldo é feita pelo Worker, usando exatamente o snapshot gravado aqui.
    /// </summary>
    public async Task<TransactionResponse> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        var (source, _, quote) = await PrepareAsync(request, cancellationToken);

        var exchange = quote.IsConversion
            ? new ExchangeSnapshot(quote.ToCurrency, quote.ConvertedAmount, quote.Rate, quote.RateId!.Value)
            : null;

        var transaction = new Transaction(
            request.SourceAccount,
            request.DestinationAccount,
            request.Amount,
            source.Currency,
            exchange,
            currentUser.UserId);

        await transactions.AddAsync(transaction, cancellationToken);

        outbox.Enqueue(new TransactionRequested(
            transaction.Id,
            transaction.SourceAccount,
            transaction.DestinationAccount,
            transaction.Amount,
            transaction.Currency,
            transaction.CreatedAt));

        audit.Record(AuditActions.TransferRequested, nameof(Transaction), transaction.Id.ToString(), TransferDetails(transaction));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TransactionResponse.FromEntity(transaction);
    }

    public async Task<TransferPreviewResponse> PreviewAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        var (_, destination, quote) = await PrepareAsync(request, cancellationToken);
        return new TransferPreviewResponse
        {
            SourceCurrency = quote.FromCurrency,
            DestinationCurrency = quote.ToCurrency,
            Amount = quote.Amount,
            DestinationAmount = quote.ConvertedAmount,
            ExchangeRate = quote.Rate,
            ExchangeRateId = quote.RateId,
            IsConversion = quote.IsConversion,
            DestinationHolder = MaskHolder(destination.HolderName)
        };
    }

    /// <summary>
    /// Validações comuns à prévia e à criação. A consulta prévia falha rápido se as contas não
    /// existirem e define as moedas; o Worker revalida situação, saldo e moedas no processamento.
    /// </summary>
    private async Task<(Account Source, Account Destination, ExchangeQuote Quote)> PrepareAsync(
        CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var source = await accounts.GetByNumberAsync(request.SourceAccount, cancellationToken);
        // Conta de origem fora do escopo responde como inexistente (não revela contas de terceiros).
        if (source is null || !AccessScope.For(currentUser).CanAccess(source))
            throw new DomainException(TransferFailureReasons.SourceNotFound);

        var destination = await accounts.GetByNumberAsync(request.DestinationAccount, cancellationToken)
            ?? throw new DomainException(TransferFailureReasons.DestinationNotFound);

        // Taxa vigente, conversão e arredondamento: tudo no backend, a partir das moedas das contas.
        var quote = await exchangeRates.QuoteAsync(source.Currency, destination.Currency, request.Amount, cancellationToken);
        return (source, destination, quote);
    }

    /// <summary>"Bruno Lima Souza" → "Bruno S.": suficiente para confirmar sem expor o nome completo.</summary>
    private static string MaskHolder(string holderName)
    {
        var parts = holderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}."
        };
    }

    /// <summary>Maior período aceito por GET /api/accounts/{id}/cash-flow (≈ 2 anos).</summary>
    public const int MaxCashFlowDays = 731;

    /// <summary>Faixa de fusos horários reais (UTC−14 a UTC+14).</summary>
    public const int MaxUtcOffsetMinutes = 14 * 60;

    public async Task<PagedResult<TransactionResponse>> ListAsync(
        TransactionListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var filter = new TransactionListFilter(
            ParseStatuses(query.Status),
            string.IsNullOrWhiteSpace(query.AccountNumber) ? null : query.AccountNumber.Trim(),
            await VisibleAccountsAsync(cancellationToken));

        var result = await transactions.ListAsync(filter, page, cancellationToken);
        return result.Map(TransactionResponse.FromEntity);
    }

    public async Task<TransactionSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var counts = await transactions.CountByStatusAsync(await VisibleAccountsAsync(cancellationToken), cancellationToken);
        int Count(TransactionStatus status) => counts.TryGetValue(status, out var n) ? n : 0;

        return new TransactionSummaryResponse
        {
            TotalTransactions = counts.Values.Sum(),
            Pending = Count(TransactionStatus.Pending),
            Processing = Count(TransactionStatus.Processing),
            Completed = Count(TransactionStatus.Completed),
            Failed = Count(TransactionStatus.Failed),
            Cancelled = Count(TransactionStatus.Cancelled)
        };
    }

    public async Task<AccountCashFlowResponse> GetAccountCashFlowAsync(
        Guid accountId, AccountCashFlowQuery query, CancellationToken cancellationToken = default)
    {
        var offsetMinutes = query.UtcOffsetMinutes ?? 0;
        if (Math.Abs(offsetMinutes) > MaxUtcOffsetMinutes)
            throw Invalid(nameof(query.UtcOffsetMinutes), $"O deslocamento deve estar entre -{MaxUtcOffsetMinutes} e {MaxUtcOffsetMinutes} minutos.");

        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var to = query.To ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(offset).DateTime);
        var from = query.From ?? to.AddDays(-29);

        if (from > to)
            throw Invalid(nameof(query.From), "A data inicial deve ser anterior ou igual à final.");
        if (to.DayNumber - from.DayNumber + 1 > MaxCashFlowDays)
            throw Invalid(nameof(query.From), $"O período máximo é de {MaxCashFlowDays} dias.");

        var account = await accounts.GetByIdAsync(accountId, cancellationToken);
        if (account is null || !AccessScope.For(currentUser).CanAccess(account))
            throw new NotFoundException($"Conta '{accountId}' não encontrada.");

        // Limites do período no fuso do cliente: [from 00:00, (to + 1) 00:00).
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), offset);
        var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), offset);

        var days = await transactions.GetDailyCashFlowAsync(
            account.AccountNumber, start, endExclusive, offsetMinutes, cancellationToken);

        return new AccountCashFlowResponse
        {
            AccountId = account.Id,
            AccountNumber = account.AccountNumber,
            Currency = account.Currency,
            From = from,
            To = to,
            UtcOffsetMinutes = offsetMinutes,
            Days = days.OrderBy(d => d.Date).ToList()
        };
    }

    /// <summary>
    /// Converte os status do query string. Aceita nomes (sem diferenciar maiúsculas), repetidos
    /// ou separados por vírgula; números e nomes inexistentes geram 400 em vez de serem ignorados.
    /// </summary>
    private static IReadOnlyCollection<TransactionStatus> ParseStatuses(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0)
            return [];

        var result = new HashSet<TransactionStatus>();
        foreach (var token in raw.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var isName = !char.IsDigit(token[0]) && token[0] != '-';
            if (!isName || !Enum.TryParse<TransactionStatus>(token, ignoreCase: true, out var status) || !Enum.IsDefined(status))
                throw Invalid("Status", $"Status inválido: '{token}'. Use: {string.Join(", ", Enum.GetNames<TransactionStatus>())}.");
            result.Add(status);
        }
        return result;
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);

    public async Task<TransactionResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransactionResponse.FromEntity(await FindOrThrowAsync(id, cancellationToken));

    public async Task<TransactionStatusResponse> GetStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransactionStatusResponse.FromEntity(await FindOrThrowAsync(id, cancellationToken));

    /// <summary>
    /// Solicita o cancelamento. Só é aceito enquanto a transação está Pending:
    /// depois que o Worker iniciou o processamento, o dinheiro pode já ter sido movido.
    /// O Customer só cancela transferências que saem de contas dele.
    /// </summary>
    public async Task<TransactionResponse> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await FindOrThrowAsync(id, cancellationToken);

        var visible = await VisibleAccountsAsync(cancellationToken);
        if (visible is not null && !visible.Contains(transaction.SourceAccount))
            throw new ForbiddenException("Somente quem envia a transferência pode cancelá-la.");

        transaction.Cancel();
        audit.Record(AuditActions.TransferCancelled, nameof(Transaction), transaction.Id.ToString(), TransferDetails(transaction));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TransactionResponse.FromEntity(transaction);
    }

    private async Task<Transaction> FindOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetByIdAsync(id, cancellationToken);
        var visible = transaction is null ? null : await VisibleAccountsAsync(cancellationToken);

        if (transaction is null
            || (visible is not null && !visible.Contains(transaction.SourceAccount) && !visible.Contains(transaction.DestinationAccount)))
            throw new NotFoundException($"Transação '{id}' não encontrada.");

        return transaction;
    }

    /// <summary>Contas visíveis ao usuário (nulo = sem restrição). Derivado do AccessScope central.</summary>
    private async Task<IReadOnlyCollection<string>?> VisibleAccountsAsync(CancellationToken cancellationToken)
    {
        var scope = AccessScope.For(currentUser);
        if (scope.IsUnrestricted)
            return null;
        return (await accounts.ListNumbersByOwnerAsync(scope.OwnerUserId!, cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    internal static object TransferDetails(Transaction t) => new
    {
        t.SourceAccount,
        t.DestinationAccount,
        t.Amount,
        t.Currency,
        t.DestinationAmount,
        t.DestinationCurrency,
        t.ExchangeRate,
        t.ExchangeRateId,
        t.RequestedByUserId,
        Status = t.Status.ToString(),
        t.FailureReason
    };
}
