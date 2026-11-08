using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Users;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Exceptions;
using FinFlow.UnitTests.Mocks;
using FluentValidation;
using Moq;

namespace FinFlow.UnitTests.Application;

public class ExchangeRateServiceTests
{
    private readonly Mock<IExchangeRateRepository> _rates = new();
    private readonly Mock<ICurrencyService> _currencies = new();
    private readonly Mock<IUserDirectory> _users = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _audit = new();
    private readonly ExchangeRateService _service;

    public ExchangeRateServiceTests()
    {
        _service = new ExchangeRateService(_rates.Object, _currencies.Object, _users.Object, _unitOfWork.Object,
            _audit.Object, FakeCurrentUser.Administrator("admin-1"), new SetExchangeRateRequestValidator());

        foreach (var c in new[] { TransactionMockData.Brl(), TransactionMockData.Usd(), TransactionMockData.Eur() })
            _currencies.Setup(s => s.GetActiveOrThrowAsync(c.Code, It.IsAny<CancellationToken>())).ReturnsAsync(c);
    }

    private ExchangeRate Vigente(string de, string para, decimal taxa)
    {
        var rate = new ExchangeRate(de, para, taxa, "admin-1");
        _rates.Setup(r => r.GetActiveAsync(de, para, It.IsAny<CancellationToken>())).ReturnsAsync(rate);
        return rate;
    }

    // ---------- Conversão ----------

    [Fact]
    public async Task Cotacao_UsdParaBrl_ExemploDaEspecificacao()
    {
        var rate = Vigente("USD", "BRL", 5.20m);

        var quote = await _service.QuoteAsync("USD", "BRL", 100.00m);

        Assert.Equal(520.00m, quote.ConvertedAmount);
        Assert.Equal(5.20m, quote.Rate);
        Assert.Equal(rate.Id, quote.RateId);
        Assert.True(quote.IsConversion);
    }

    [Theory]
    [InlineData(10.01, 6.1234, 61.30)]   // 61.295234 → 61.30
    [InlineData(0.01, 0.4999, 0.00)]     // seria zero → recusado (ver teste abaixo)
    [InlineData(33.33, 5.555555, 185.17)] // 185.1666... → 185.17
    public async Task Cotacao_EurParaBrl_ArredondaNasCasasDoDestino(decimal valor, decimal taxa, decimal esperado)
    {
        Vigente("EUR", "BRL", taxa);
        if (esperado == 0m)
        {
            await Assert.ThrowsAsync<DomainException>(() => _service.QuoteAsync("EUR", "BRL", valor));
            return;
        }

        Assert.Equal(esperado, (await _service.QuoteAsync("EUR", "BRL", valor)).ConvertedAmount);
    }

    [Fact]
    public async Task Cotacao_MesmaMoeda_SemTaxaEValorIgual()
    {
        var quote = await _service.QuoteAsync("BRL", "BRL", 150.50m);

        Assert.Equal(150.50m, quote.ConvertedAmount);
        Assert.Equal(1m, quote.Rate);
        Assert.Null(quote.RateId);
        _rates.Verify(r => r.GetActiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cotacao_SemTaxaVigente_Recusa()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.QuoteAsync("USD", "EUR", 10m));
        Assert.Contains("USD para EUR", ex.Message);
    }

    [Fact]
    public async Task Cotacao_NaoUsaATaxaInversa()
    {
        Vigente("USD", "BRL", 5.20m); // só existe USD→BRL

        await Assert.ThrowsAsync<DomainException>(() => _service.QuoteAsync("BRL", "USD", 520m));
    }

    [Fact]
    public async Task Cotacao_MoedaDesativada_Recusa()
    {
        _currencies.Setup(s => s.GetActiveOrThrowAsync("EUR", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("A moeda 'EUR' está desativada."));

        await Assert.ThrowsAsync<DomainException>(() => _service.QuoteAsync("EUR", "BRL", 10m));
    }

    [Fact]
    public async Task Cotacao_ValorComCasasDemaisParaAMoeda_Recusa() =>
        await Assert.ThrowsAsync<DomainException>(() => _service.QuoteAsync("USD", "BRL", 10.001m));

    // ---------- Administração de taxas ----------

    [Fact]
    public async Task Definir_EncerraAVigenteECriaNovaVersao_ComAuditoria()
    {
        var anterior = Vigente("USD", "BRL", 5.20m);
        ExchangeRate? nova = null;
        _rates.Setup(r => r.AddAsync(It.IsAny<ExchangeRate>(), It.IsAny<CancellationToken>()))
            .Callback<ExchangeRate, CancellationToken>((r, _) => nova = r);

        var response = await _service.SetAsync(new SetExchangeRateRequest { FromCurrency = "usd", ToCurrency = "brl", Rate = 5.35m });

        Assert.False(anterior.IsActive);
        Assert.Equal(5.20m, anterior.Rate); // versão antiga preservada, só encerrada
        Assert.True(nova!.IsActive);
        Assert.Equal(5.35m, response.Rate);
        Assert.Equal("admin-1", nova.CreatedByUserId);
        _audit.Verify(a => a.Record(AuditActions.ExchangeRateSet, nameof(ExchangeRate), nova.Id.ToString(),
            It.IsAny<object?>(), AuditResults.Succeeded), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("USD", "USD", 1)]
    [InlineData("USD", "BRL", 0)]
    [InlineData("USD", "BRL", -1)]
    [InlineData("US", "BRL", 5)]
    public async Task Definir_DadosInvalidos_Recusa(string de, string para, decimal taxa) =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SetAsync(new SetExchangeRateRequest { FromCurrency = de, ToCurrency = para, Rate = taxa }));

    [Fact]
    public async Task Definir_AlteracaoSimultanea_ViraConflito()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException("duplicada"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.SetAsync(new SetExchangeRateRequest { FromCurrency = "USD", ToCurrency = "BRL", Rate = 5.3m }));
    }
}
