using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Messaging;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Exceptions;
using FinFlow.UnitTests.Mocks;
using FluentValidation;
using Moq;

namespace FinFlow.UnitTests.Application;

public class TransactionServiceTests
{
    // Tudo mockado: nenhum banco de dados ou RabbitMQ é acessado nestes testes.
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IAccountRepository> _accounts = new();
    private readonly Mock<IMessageOutbox> _outbox = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IExchangeRateService> _rates = new();
    private readonly Mock<IAuditTrail> _audit = new();
    private ICurrentUser _currentUser = FakeCurrentUser.System();
    private readonly TransactionService _service;

    public TransactionServiceTests()
    {
        _service = new TransactionService(
            _transactions.Object, _accounts.Object, _outbox.Object, _unitOfWork.Object,
            new CreateTransactionRequestValidator(), _rates.Object, new DelegatingUser(() => _currentUser), _audit.Object);

        // Mesma moeda por padrão: cotação sem câmbio (o serviço real valida moedas e casas decimais).
        _rates.Setup(r => r.QuoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string from, string to, decimal amount, CancellationToken _) =>
                new ExchangeQuote { FromCurrency = from, ToCurrency = to, Amount = amount, ConvertedAmount = amount, Rate = 1m });

        // Contas Ana (12345) e Bruno (67890) existem.
        _accounts.Setup(r => r.GetByNumberAsync(TransactionMockData.ContaAna, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionMockData.Ana());
        _accounts.Setup(r => r.GetByNumberAsync(TransactionMockData.ContaBruno, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionMockData.Bruno());
    }

    // ---------- POST /api/transactions ----------

    [Fact]
    public async Task Criar_RequestValido_DeveRetornarTransacaoPendenteEmBRL()
    {
        var response = await _service.CreateAsync(TransactionMockData.RequestValido());

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal("12345", response.SourceAccount);
        Assert.Equal("67890", response.DestinationAccount);
        Assert.Equal(500.00m, response.Amount);
        Assert.Equal("BRL", response.Currency);
        Assert.Equal("Pending", response.Status);
    }

    [Fact]
    public async Task Criar_RequestValido_DeveGravarTransacaoEEventoNoMesmoSaveChanges()
    {
        Transaction? salva = null;
        TransactionRequested? evento = null;
        _transactions
            .Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => salva = t);
        _outbox
            .Setup(o => o.Enqueue(It.IsAny<TransactionRequested>()))
            .Callback<TransactionRequested>(e => evento = e);

        var response = await _service.CreateAsync(TransactionMockData.RequestValido());

        Assert.Equal(response.Id, salva!.Id);
        Assert.Equal(response.Id, evento!.TransactionId);
        Assert.Equal(500.00m, evento.Amount);
        // Um único SaveChanges = transação e evento gravados atomicamente.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Criar_MoedaDaTransacao_DeveVirDaContaDeOrigem()
    {
        _accounts.Setup(r => r.GetByNumberAsync(TransactionMockData.ContaCarla, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionMockData.Carla()); // USD

        var request = TransactionMockData.RequestValido() with { SourceAccount = TransactionMockData.ContaCarla };
        var response = await _service.CreateAsync(request);

        Assert.Equal("USD", response.Currency);
    }

    [Theory]
    [InlineData("00000", TransactionMockData.ContaBruno, "Conta de origem não encontrada.")]
    [InlineData(TransactionMockData.ContaAna, "00000", "Conta de destino não encontrada.")]
    public async Task Criar_ContaInexistente_DeveLancarDomainExceptionENaoSalvar(
        string origem, string destino, string mensagem)
    {
        var request = TransactionMockData.RequestValido() with { SourceAccount = origem, DestinationAccount = destino };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(request));

        Assert.Equal(mensagem, ex.Message);
        _outbox.Verify(o => o.Enqueue(It.IsAny<TransactionRequested>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Criar_RequestInvalido_DeveLancarValidationExceptionENaoSalvar()
    {
        var request = new CreateTransactionRequest
        {
            SourceAccount = "12345",
            DestinationAccount = "12345",
            Amount = -1
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request));

        Assert.Equal(2, ex.Errors.Count());
        _transactions.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- GET /api/transactions ----------

    [Fact]
    public async Task Listar_ComTransacoesMockadas_DeveRetornarTodas()
    {
        var mock = TransactionMockData.ListaDeTransacoes();
        var page = PageRequest.Default;
        _transactions
            .Setup(r => r.ListAsync(TransactionListFilter.None, page, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<Transaction>.Create(mock, page, mock.Count));

        var response = await _service.ListAsync(new TransactionListQuery(), page);

        Assert.Equal(5, response.Items.Count);
        Assert.Equal(mock.Select(t => t.Id), response.Items.Select(r => r.Id));
        Assert.Equal(11_916.55m, response.Items.Sum(r => r.Amount));
        Assert.Equal(4, response.Items.Count(r => r.Status == "Pending"));
        Assert.Equal(1, response.Items.Count(r => r.Status == "Cancelled"));
        Assert.Equal(5, response.TotalItems);
    }

    [Fact]
    public async Task Listar_SemTransacoes_DeveRetornarListaVazia()
    {
        _transactions
            .Setup(r => r.ListAsync(It.IsAny<TransactionListFilter>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<Transaction>.Create([], PageRequest.Default, 0));

        var response = await _service.ListAsync(new TransactionListQuery(), PageRequest.Default);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalItems);
        Assert.Equal(0, response.TotalPages);
        Assert.False(response.HasNextPage);
    }

    [Theory]
    [InlineData(new[] { "Pending" }, new[] { TransactionStatus.Pending })]
    [InlineData(new[] { "pending", "PROCESSING" }, new[] { TransactionStatus.Pending, TransactionStatus.Processing })]
    [InlineData(new[] { "Pending,Processing" }, new[] { TransactionStatus.Pending, TransactionStatus.Processing })]
    [InlineData(new[] { " Failed , Cancelled ", "Failed" }, new[] { TransactionStatus.Failed, TransactionStatus.Cancelled })]
    public async Task Listar_ConverteFiltrosDeStatusEConta(string[] status, TransactionStatus[] esperados)
    {
        TransactionListFilter? filtro = null;
        _transactions
            .Setup(r => r.ListAsync(It.IsAny<TransactionListFilter>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionListFilter, PageRequest, CancellationToken>((f, _, _) => filtro = f)
            .ReturnsAsync(PagedResult<Transaction>.Create([], PageRequest.Default, 0));

        await _service.ListAsync(new TransactionListQuery { Status = status, AccountNumber = " 12345 " }, PageRequest.Default);

        Assert.Equal(esperados.Order(), filtro!.Statuses.Order());
        Assert.Equal("12345", filtro.AccountNumber);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("Pending,99")]
    public async Task Listar_StatusInvalido_LancaValidationSemConsultar(string status)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _service.ListAsync(new TransactionListQuery { Status = [status] }, PageRequest.Default));

        Assert.Contains(ex.Errors, e => e.PropertyName == "Status");
        _transactions.Verify(
            r => r.ListAsync(It.IsAny<TransactionListFilter>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------- GET /api/transactions/summary ----------

    [Fact]
    public async Task Resumo_PreencheStatusAusentesComZero()
    {
        _transactions.Setup(r => r.CountByStatusAsync(It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            new Dictionary<TransactionStatus, int> { [TransactionStatus.Pending] = 3, [TransactionStatus.Completed] = 5 });

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(8, summary.TotalTransactions);
        Assert.Equal(3, summary.Pending);
        Assert.Equal(5, summary.Completed);
        Assert.Equal(0, summary.Processing);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Cancelled);
    }

    // ---------- GET /api/accounts/{id}/cash-flow ----------

    [Fact]
    public async Task FluxoDeCaixa_ConverteOPeriodoLocalEmLimitesUtc()
    {
        var ana = TransactionMockData.Ana();
        _accounts.Setup(r => r.GetByIdAsync(ana.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ana);
        DateTimeOffset inicio = default, fim = default;
        _transactions
            .Setup(r => r.GetDailyCashFlowAsync(ana.AccountNumber, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), -180, It.IsAny<CancellationToken>()))
            .Callback<string, DateTimeOffset, DateTimeOffset, int, CancellationToken>((_, f, t, _, _) => (inicio, fim) = (f, t))
            .ReturnsAsync(
            [
                new DailyCashFlow { Date = new DateOnly(2026, 11, 6), Inflow = 0m, Outflow = 500m },
                new DailyCashFlow { Date = new DateOnly(2026, 11, 2), Inflow = 120m, Outflow = 0m }
            ]);

        var response = await _service.GetAccountCashFlowAsync(ana.Id, new AccountCashFlowQuery
        {
            From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 7), UtcOffsetMinutes = -180
        });

        // 01/11 00:00 em UTC-3 = 01/11 03:00Z; fim exclusivo = 08/11 00:00 em UTC-3.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero), inicio.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero), fim.ToUniversalTime());
        Assert.Equal("BRL", response.Currency);
        Assert.Equal([new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6)], response.Days.Select(d => d.Date));
    }

    [Fact]
    public async Task FluxoDeCaixa_SemPeriodo_UsaUltimos30Dias()
    {
        var ana = TransactionMockData.Ana();
        _accounts.Setup(r => r.GetByIdAsync(ana.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ana);
        _transactions
            .Setup(r => r.GetDailyCashFlowAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _service.GetAccountCashFlowAsync(ana.Id, new AccountCashFlowQuery());

        Assert.Equal(29, response.To.DayNumber - response.From.DayNumber);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), response.To);
    }

    [Theory]
    [InlineData("2026-11-07", "2026-11-01", 0, "From")]
    [InlineData("2024-01-01", "2026-11-07", 0, "From")]
    [InlineData("2026-11-01", "2026-11-07", 900, "UtcOffsetMinutes")]
    public async Task FluxoDeCaixa_ParametrosInvalidos_LancaValidation(string de, string ate, int offset, string campo)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.GetAccountCashFlowAsync(Guid.NewGuid(),
            new AccountCashFlowQuery { From = DateOnly.Parse(de), To = DateOnly.Parse(ate), UtcOffsetMinutes = offset }));

        Assert.Contains(ex.Errors, e => e.PropertyName == campo);
    }

    [Fact]
    public async Task FluxoDeCaixa_ContaInexistente_LancaNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetAccountCashFlowAsync(Guid.NewGuid(), new AccountCashFlowQuery()));
    }

    // ---------- GET /api/transactions/{id} e /status ----------

    [Fact]
    public async Task ConsultarPorId_Existente_DeveRetornarTransacao()
    {
        var transaction = TransactionMockData.TransacaoPendente();
        SetupGetById(transaction);

        var response = await _service.GetByIdAsync(transaction.Id);

        Assert.Equal(transaction.Id, response.Id);
        Assert.Equal(500.00m, response.Amount);
        Assert.Equal("Pending", response.Status);
    }

    [Fact]
    public async Task ConsultarStatus_TransacaoQueFalhou_DeveRetornarMotivo()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento();
        transaction.Fail("Saldo insuficiente na conta de origem.");
        SetupGetById(transaction);

        var status = await _service.GetStatusAsync(transaction.Id);

        Assert.Equal("Failed", status.Status);
        Assert.Equal("Saldo insuficiente na conta de origem.", status.FailureReason);
        Assert.NotNull(status.UpdatedAt);
    }

    [Fact]
    public async Task ConsultarPorId_Inexistente_DeveLancarNotFoundException()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(id));

        Assert.Equal($"Transação '{id}' não encontrada.", ex.Message);
    }

    // ---------- PATCH /api/transactions/{id}/cancel ----------

    [Fact]
    public async Task Cancelar_TransacaoPendente_DeveRetornarCancelledESalvar()
    {
        var transaction = TransactionMockData.TransacaoPendente();
        SetupGetById(transaction);

        var response = await _service.CancelAsync(transaction.Id);

        Assert.Equal("Cancelled", response.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelar_TransacaoEmProcessamento_DeveLancarENaoSalvar()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento();
        SetupGetById(transaction);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CancelAsync(transaction.Id));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancelar_TransacaoJaCancelada_DeveLancarENaoSalvar()
    {
        var transaction = TransactionMockData.TransacaoCancelada();
        SetupGetById(transaction);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CancelAsync(transaction.Id));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancelar_TransacaoInexistente_DeveLancarNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CancelAsync(Guid.NewGuid()));
    }

    private void SetupGetById(Transaction transaction) =>
        _transactions
            .Setup(r => r.GetByIdAsync(transaction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
    // ---------- Multimoedas: snapshot calculado no backend ----------

    [Fact]
    public async Task Criar_EntreMoedasDiferentes_GravaSnapshotDaCotacaoDoBackend()
    {
        var rateId = Guid.NewGuid();
        _accounts.Setup(r => r.GetByNumberAsync(TransactionMockData.ContaCarla, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionMockData.Carla()); // USD
        _rates.Setup(r => r.QuoteAsync("USD", "BRL", 100m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeQuote { FromCurrency = "USD", ToCurrency = "BRL", Amount = 100m, ConvertedAmount = 520m, Rate = 5.2m, RateId = rateId });

        var response = await _service.CreateAsync(new CreateTransactionRequest
        {
            SourceAccount = TransactionMockData.ContaCarla, DestinationAccount = TransactionMockData.ContaBruno, Amount = 100m
        });

        Assert.Equal("USD", response.Currency);
        Assert.Equal(100m, response.Amount);
        Assert.Equal("BRL", response.DestinationCurrency);
        Assert.Equal(520m, response.DestinationAmount);
        Assert.Equal(5.2m, response.ExchangeRate);
        Assert.Equal(rateId, response.ExchangeRateId);
        _audit.Verify(a => a.Record(AuditActions.TransferRequested, nameof(Transaction), response.Id.ToString(),
            It.IsAny<object?>(), AuditResults.Succeeded), Times.Once);
    }

    [Fact]
    public async Task Criar_MesmaMoeda_SnapshotSemCambio()
    {
        var response = await _service.CreateAsync(TransactionMockData.RequestValido());

        Assert.Equal("BRL", response.DestinationCurrency);
        Assert.Equal(response.Amount, response.DestinationAmount);
        Assert.Equal(1m, response.ExchangeRate);
        Assert.Null(response.ExchangeRateId);
    }

    [Fact]
    public async Task Criar_SemTaxaVigente_PropagaErroENaoSalva()
    {
        _rates.Setup(r => r.QuoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Não há taxa de câmbio vigente de USD para BRL."));

        await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(TransactionMockData.RequestValido()));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(o => o.Enqueue(It.IsAny<TransactionRequested>()), Times.Never);
    }

    // ---------- Escopo do Customer ----------

    [Fact]
    public async Task Criar_CustomerUsandoContaDeOutro_RecusaComoInexistenteENaoSalva()
    {
        _currentUser = FakeCurrentUser.Customer("cliente-x"); // Ana não pertence a ele

        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(TransactionMockData.RequestValido()));

        Assert.Equal(FinFlow.Domain.Services.TransferFailureReasons.SourceNotFound, ex.Message);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Criar_CustomerComContaPropria_RegistraOSolicitante()
    {
        _currentUser = FakeCurrentUser.Customer("cliente-ana");
        _accounts.Setup(r => r.GetByNumberAsync(TransactionMockData.ContaAna, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account(TransactionMockData.ContaAna, "Ana Souza", 1_000m, "BRL", ownerUserId: "cliente-ana"));

        var response = await _service.CreateAsync(TransactionMockData.RequestValido());

        Assert.Equal("cliente-ana", response.RequestedByUserId);
    }

    [Fact]
    public async Task Consultar_CustomerTransacaoDeTerceiros_RespondeNaoEncontrada()
    {
        var transacao = TransactionMockData.TransacaoPendente();
        _transactions.Setup(r => r.GetByIdAsync(transacao.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transacao);
        _accounts.Setup(r => r.ListNumbersByOwnerAsync("cliente-x", It.IsAny<CancellationToken>())).ReturnsAsync(["5550000001"]);
        _currentUser = FakeCurrentUser.Customer("cliente-x");

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(transacao.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetStatusAsync(transacao.Id));
    }

    [Fact]
    public async Task Cancelar_CustomerQueSoRecebe_EProibidoENaoAlteraStatus()
    {
        var transacao = TransactionMockData.TransacaoPendente(); // Ana → Bruno
        _transactions.Setup(r => r.GetByIdAsync(transacao.Id, It.IsAny<CancellationToken>())).ReturnsAsync(transacao);
        _accounts.Setup(r => r.ListNumbersByOwnerAsync("cliente-bruno", It.IsAny<CancellationToken>()))
            .ReturnsAsync([TransactionMockData.ContaBruno]);
        _currentUser = FakeCurrentUser.Customer("cliente-bruno");

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CancelAsync(transacao.Id));
        Assert.Equal(TransactionStatus.Pending, transacao.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Customer", true)]
    [InlineData("Treasurer", false)]
    [InlineData("Administrator", false)]
    public async Task Listar_AplicaEscopoDoPerfil(string perfil, bool restrito)
    {
        TransactionListFilter? filtro = null;
        _transactions.Setup(r => r.ListAsync(It.IsAny<TransactionListFilter>(), It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionListFilter, PageRequest, CancellationToken>((f, _, _) => filtro = f)
            .ReturnsAsync(PagedResult<Transaction>.Create([], PageRequest.Default, 0));
        _accounts.Setup(r => r.ListNumbersByOwnerAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(["1112223334"]);
        _currentUser = new FakeCurrentUser("u1", perfil);

        await _service.ListAsync(new TransactionListQuery(), PageRequest.Default);

        if (restrito)
            Assert.Equal(["1112223334"], filtro!.VisibleAccounts!);
        else
            Assert.Null(filtro!.VisibleAccounts);
    }
}

/// <summary>Permite trocar o usuário atual dentro de um teste depois de criar o serviço.</summary>
internal sealed class DelegatingUser(Func<ICurrentUser> current) : ICurrentUser
{
    public string? UserId => current().UserId;
    public string? Email => current().Email;
    public bool IsAuthenticated => current().IsAuthenticated;
    public bool IsInRole(string role) => current().IsInRole(role);
}
