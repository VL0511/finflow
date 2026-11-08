using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Users;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Exceptions;
using FinFlow.UnitTests.Mocks;
using FluentValidation;
using Moq;

namespace FinFlow.UnitTests.Application;

public class AccountServiceTests
{
    private readonly Mock<IAccountRepository> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAccountNumberGenerator> _numbers = new();
    private readonly Mock<ICurrencyService> _currencies = new();
    private readonly Mock<IUserDirectory> _users = new();
    private readonly Mock<IAuditTrail> _audit = new();
    private ICurrentUser _currentUser = FakeCurrentUser.System();
    private readonly AccountService _service;

    public AccountServiceTests()
    {
        _service = new AccountService(
            _repository.Object, _unitOfWork.Object, new CreateAccountRequestValidator(), _numbers.Object,
            _currencies.Object, _users.Object, new DelegatingUser(() => _currentUser), _audit.Object);

        _numbers.SetupSequence(n => n.Next()).Returns("1000000017").Returns("2000000016").Returns("3000000015")
            .Returns("4000000014").Returns("5000000013").Returns("6000000012");
        _currencies.Setup(c => c.GetActiveOrThrowAsync("BRL", It.IsAny<CancellationToken>())).ReturnsAsync(TransactionMockData.Brl());
        _currencies.Setup(c => c.GetActiveOrThrowAsync("USD", It.IsAny<CancellationToken>())).ReturnsAsync(TransactionMockData.Usd());
    }

    // ---------- Criação: número gerado pelo backend ----------

    [Fact]
    public async Task Criar_RequestValido_GeraNumeroSalvaEAudita()
    {
        Account? salva = null;
        _repository
            .Setup(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .Callback<Account, CancellationToken>((a, _) => salva = a);

        var response = await _service.CreateAsync(TransactionMockData.RequestNovaConta());

        Assert.Equal("1000000017", response.AccountNumber); // vem do gerador, não do cliente
        Assert.Equal("Fernanda Lima", response.HolderName);
        Assert.Equal(1_500.00m, response.Balance);
        Assert.Equal("BRL", response.Currency);
        Assert.True(response.IsActive);
        Assert.Equal(salva!.Id, response.Id);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.Record(AuditActions.AccountCreated, nameof(Account), response.Id.ToString(),
            It.IsAny<object?>(), AuditResults.Succeeded), Times.Once);
    }

    [Fact]
    public async Task Criar_NumeroGeradoJaExistente_GeraOutroNumero()
    {
        _repository.Setup(r => r.ExistsByNumberAsync("1000000017", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var response = await _service.CreateAsync(TransactionMockData.RequestNovaConta());

        Assert.Equal("2000000016", response.AccountNumber);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Criar_ColisaoNoIndiceUnico_DescartaETentaComNovoNumero()
    {
        // Cadastro simultâneo gravou o mesmo número entre a checagem e o INSERT.
        _unitOfWork.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException("duplicado"))
            .Returns(Task.CompletedTask);

        var response = await _service.CreateAsync(TransactionMockData.RequestNovaConta());

        Assert.Equal("2000000016", response.AccountNumber);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Criar_ColisoesPersistentes_DesisteAposOLimite()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException("duplicado"));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _service.CreateAsync(TransactionMockData.RequestNovaConta()));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(AccountService.MaxNumberAttempts));
    }

    [Theory]
    [InlineData("", 100, "BRL")]          // sem titular
    [InlineData("Fernanda", -1, "BRL")]   // saldo negativo
    [InlineData("Fernanda", 100, "R$")]   // código de moeda inválido
    [InlineData("Fernanda", 1.234, "BRL")] // casas demais
    public async Task Criar_RequestInvalido_DeveLancarValidationException(string titular, decimal saldo, string moeda)
    {
        var request = new CreateAccountRequest { HolderName = titular, InitialBalance = saldo, Currency = moeda };

        await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Criar_MoedaInexistenteOuDesativada_Recusa()
    {
        _currencies.Setup(c => c.GetActiveOrThrowAsync("JPY", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Moeda 'JPY' não cadastrada."));

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(TransactionMockData.RequestNovaConta() with { Currency = "JPY" }));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Dono da conta ----------

    [Fact]
    public async Task Criar_Customer_ContaSempreDeleMesmo_IgnoraDonoInformado()
    {
        _currentUser = FakeCurrentUser.Customer("cliente-1");

        var response = await _service.CreateAsync(TransactionMockData.RequestNovaConta() with { OwnerUserId = "outro-usuario" });

        Assert.Equal("cliente-1", response.OwnerUserId);
    }

    [Fact]
    public async Task Criar_Administrator_ParaUsuarioAtivo()
    {
        _currentUser = FakeCurrentUser.Administrator();
        _users.Setup(u => u.IsActiveUserAsync("cliente-9", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var response = await _service.CreateAsync(TransactionMockData.RequestNovaConta() with { OwnerUserId = "cliente-9" });

        Assert.Equal("cliente-9", response.OwnerUserId);
    }

    [Fact]
    public async Task Criar_Administrator_DonoInexistenteOuInativo_Recusa()
    {
        _currentUser = FakeCurrentUser.Administrator();

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(TransactionMockData.RequestNovaConta() with { OwnerUserId = "fantasma" }));
    }

    // ---------- Consultas e escopo ----------

    [Fact]
    public async Task Listar_DeveRetornarContasMockadas()
    {
        var page = PageRequest.Default;
        _repository.Setup(r => r.ListAsync(page, It.IsAny<AccessScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<Account>.Create(TransactionMockData.ListaDeContas(), page, totalItems: 4));

        var response = await _service.ListAsync(page);

        Assert.Equal(4, response.Items.Count);
        Assert.Equal(["12345", "67890", "11223", "44556"], response.Items.Select(a => a.AccountNumber));
        Assert.Single(response.Items, a => !a.IsActive);
        Assert.Single(response.Items, a => a.Currency == "USD");
        Assert.Equal(4, response.TotalItems);
        Assert.Equal(1, response.TotalPages);
    }

    [Fact]
    public async Task Listar_RepassaPaginaAoRepositorioEPreservaMetadados()
    {
        var page = PageRequest.Create(page: 2, pageSize: 2);
        _repository.Setup(r => r.ListAsync(page, It.IsAny<AccessScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<Account>.Create([TransactionMockData.Carla(), TransactionMockData.Diego()], page, totalItems: 5));

        var response = await _service.ListAsync(page);

        Assert.Equal(["11223", "44556"], response.Items.Select(a => a.AccountNumber));
        Assert.Equal(2, response.Page);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalItems);
        Assert.Equal(3, response.TotalPages);
        Assert.True(response.HasPreviousPage);
        Assert.True(response.HasNextPage);
    }

    [Theory]
    [InlineData("Customer", "u1")]
    [InlineData("Treasurer", null)]
    [InlineData("Administrator", null)]
    public async Task Listar_EResumo_AplicamEscopoDoPerfil(string perfil, string? donoEsperado)
    {
        AccessScope? escopoLista = null, escopoResumo = null;
        _repository.Setup(r => r.ListAsync(It.IsAny<PageRequest>(), It.IsAny<AccessScope>(), It.IsAny<CancellationToken>()))
            .Callback<PageRequest, AccessScope, CancellationToken>((_, s, _) => escopoLista = s)
            .ReturnsAsync(PagedResult<Account>.Create([], PageRequest.Default, 0));
        _repository.Setup(r => r.SummarizeByCurrencyAsync(It.IsAny<AccessScope>(), It.IsAny<CancellationToken>()))
            .Callback<AccessScope, CancellationToken>((s, _) => escopoResumo = s)
            .ReturnsAsync([]);
        _currentUser = new FakeCurrentUser("u1", perfil);

        await _service.ListAsync(PageRequest.Default);
        await _service.GetSummaryAsync();

        Assert.Equal(donoEsperado, escopoLista!.OwnerUserId);
        Assert.Equal(donoEsperado, escopoResumo!.OwnerUserId);
    }

    [Fact]
    public async Task Consultar_CustomerContaDeOutro_RespondeNaoEncontrada()
    {
        var ana = new Account("12345", "Ana Souza", 1_000m, "BRL", ownerUserId: "cliente-ana");
        _repository.Setup(r => r.GetByIdAsync(ana.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ana);
        _currentUser = FakeCurrentUser.Customer("cliente-intruso");

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(ana.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetBalanceAsync(ana.Id));

        _currentUser = FakeCurrentUser.Customer("cliente-ana");
        Assert.Equal(1_000m, (await _service.GetBalanceAsync(ana.Id)).Balance);
    }

    [Fact]
    public async Task Resumo_SomaQuantidadesEOrdenaMoedas_SemMisturarMoedas()
    {
        _repository.Setup(r => r.SummarizeByCurrencyAsync(It.IsAny<AccessScope>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AccountCurrencySummary { Currency = "USD", Accounts = 1, ActiveAccounts = 1, TotalBalance = 5_000m },
            new AccountCurrencySummary { Currency = "BRL", Accounts = 3, ActiveAccounts = 2, TotalBalance = 1_550m }
        ]);

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(4, summary.TotalAccounts);
        Assert.Equal(3, summary.ActiveAccounts);
        Assert.Equal(1, summary.InactiveAccounts);
        Assert.Equal(["BRL", "USD"], summary.Balances.Select(b => b.Currency));
        Assert.Equal(1_550m, summary.Balances[0].TotalBalance);
        Assert.Equal(5_000m, summary.Balances[1].TotalBalance);
    }

    [Fact]
    public async Task Resumo_SemContas_RetornaZeros()
    {
        _repository.Setup(r => r.SummarizeByCurrencyAsync(It.IsAny<AccessScope>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(0, summary.TotalAccounts);
        Assert.Empty(summary.Balances);
    }

    [Fact]
    public async Task ConsultarSaldo_ContaExistente_DeveRetornarSaldoEMoeda()
    {
        var ana = TransactionMockData.Ana();
        _repository.Setup(r => r.GetByIdAsync(ana.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ana);

        var balance = await _service.GetBalanceAsync(ana.Id);

        Assert.Equal(ana.Id, balance.AccountId);
        Assert.Equal("12345", balance.AccountNumber);
        Assert.Equal(1_000.00m, balance.Balance);
        Assert.Equal("BRL", balance.Currency);
    }

    [Fact]
    public async Task Consultar_ContaInexistente_DeveLancarNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetBalanceAsync(Guid.NewGuid()));
    }
}
