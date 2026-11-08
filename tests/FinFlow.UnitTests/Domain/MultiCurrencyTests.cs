using FinFlow.Application.Accounts;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;

namespace FinFlow.UnitTests.Domain;

public class MultiCurrencyTests
{
    private static readonly Guid RateId = Guid.NewGuid();

    // ---------- Moeda ----------

    [Theory]
    [InlineData(2, 10.005, 10.01)]   // meio arredonda para longe do zero
    [InlineData(2, 10.004, 10.00)]
    [InlineData(0, 10.5, 11)]        // moeda sem casas (ex.: JPY)
    [InlineData(2, 520, 520)]
    public void Moeda_ArredondaNasCasasDaMoeda(int casas, decimal valor, decimal esperado) =>
        Assert.Equal(esperado, new Currency("XTS", "Teste", casas).Round(valor));

    [Theory]
    [InlineData("US", 2)]   // código curto
    [InlineData("US1", 2)]  // dígito
    [InlineData("USD", 3)]  // mais casas que a precisão das colunas
    [InlineData("USD", -1)]
    public void Moeda_DadosInvalidos_Recusa(string codigo, int casas) =>
        Assert.Throws<ArgumentException>(() => new Currency(codigo, "Teste", casas));

    [Fact]
    public void Moeda_CodigoNormalizado() => Assert.Equal("GBP", new Currency(" gbp ", "Libra", 2).Code);

    // ---------- Taxa de câmbio ----------

    [Theory]
    [InlineData("USD", "USD", 1)]      // mesma moeda
    [InlineData("USD", "BRL", 0)]      // zero
    [InlineData("USD", "BRL", -5.2)]   // negativa
    [InlineData("USD", "BRL", 5.123456789)] // mais de 8 casas
    public void Taxa_Invalida_Recusa(string de, string para, decimal taxa) =>
        Assert.Throws<ArgumentException>(() => new ExchangeRate(de, para, taxa, "admin"));

    [Fact]
    public void Taxa_Supersede_EncerraSemAlterarValor_EApenasUmaVez()
    {
        var rate = new ExchangeRate("USD", "BRL", 5.20m, "admin");

        rate.Supersede("admin-2");

        Assert.False(rate.IsActive);
        Assert.Equal(5.20m, rate.Rate);
        Assert.Equal("admin-2", rate.SupersededByUserId);
        Assert.NotNull(rate.SupersededAt);
        Assert.Throws<InvalidOperationException>(() => rate.Supersede("admin"));
    }

    // ---------- Snapshot na transação ----------

    [Fact]
    public void Transacao_MesmaMoeda_SnapshotSemCambio()
    {
        var t = new Transaction("1", "2", 100m, "EUR");

        Assert.Equal("EUR", t.DestinationCurrency);
        Assert.Equal(100m, t.DestinationAmount);
        Assert.Equal(1m, t.ExchangeRate);
        Assert.Null(t.ExchangeRateId);
        Assert.False(t.IsCrossCurrency);
    }

    [Theory]
    [InlineData("USD", 520, 5.2)]   // destino igual à origem
    [InlineData("BRL", 0, 5.2)]     // convertido zero
    [InlineData("BRL", 520, 0)]     // taxa zero
    public void Transacao_SnapshotInvalido_Recusa(string destino, decimal convertido, decimal taxa) =>
        Assert.Throws<ArgumentException>(() =>
            new Transaction("1", "2", 100m, "USD", new ExchangeSnapshot(destino, convertido, taxa, RateId)));

    // ---------- Movimentação com câmbio ----------

    [Fact]
    public void Transferir_UsdParaBrl_DebitaNaOrigemECreditaOConvertido()
    {
        var origem = new Account("1", "Carla", 1_000m, "USD");
        var destino = new Account("2", "Bruno", 10m, "BRL");
        var t = Processando(new Transaction("1", "2", 100m, "USD", new ExchangeSnapshot("BRL", 520m, 5.2m, RateId)));

        var result = FundsTransfer.Execute(t, origem, destino);

        Assert.True(result.Succeeded);
        Assert.Equal(900m, origem.Balance);   // USD 100 debitados
        Assert.Equal(530m, destino.Balance);  // BRL 520 creditados
        Assert.Equal(TransactionStatus.Completed, t.Status);
    }

    [Fact]
    public void Transferir_MoedaDoDestinoDiferenteDoSnapshot_FalhaSemMovimentar()
    {
        // Snapshot diz BRL, mas a conta de destino é EUR: nada é movimentado.
        var origem = new Account("1", "Carla", 1_000m, "USD");
        var destino = new Account("2", "Elisa", 0m, "EUR");
        var t = Processando(new Transaction("1", "2", 100m, "USD", new ExchangeSnapshot("BRL", 520m, 5.2m, RateId)));

        var result = FundsTransfer.Execute(t, origem, destino);

        Assert.False(result.Succeeded);
        Assert.Equal(TransferFailureReasons.IncompatibleCurrencies("USD", "EUR", "USD→BRL"), t.FailureReason);
        Assert.Equal(1_000m, origem.Balance);
        Assert.Equal(0m, destino.Balance);
    }

    [Fact]
    public void Transferir_ComCambio_SaldoInsuficienteNaMoedaDeOrigem_Falha()
    {
        var origem = new Account("1", "Carla", 99.99m, "USD");
        var destino = new Account("2", "Bruno", 0m, "BRL");
        var t = Processando(new Transaction("1", "2", 100m, "USD", new ExchangeSnapshot("BRL", 520m, 5.2m, RateId)));

        var result = FundsTransfer.Execute(t, origem, destino);

        Assert.False(result.Succeeded);
        Assert.Equal(TransferFailureReasons.InsufficientFunds, t.FailureReason);
        Assert.Equal(99.99m, origem.Balance);
        Assert.Equal(0m, destino.Balance);
    }

    private static Transaction Processando(Transaction t)
    {
        t.StartProcessing();
        return t;
    }

    // ---------- Número de conta ----------

    [Fact]
    public void NumeroDeConta_DezDigitosSemZeroInicialEVerificadorValido()
    {
        var generator = new RandomAccountNumberGenerator();
        var numeros = Enumerable.Range(0, 2_000).Select(_ => generator.Next()).ToList();

        Assert.All(numeros, n =>
        {
            Assert.Equal(RandomAccountNumberGenerator.Length, n.Length);
            Assert.All(n, c => Assert.True(char.IsAsciiDigit(c)));
            Assert.NotEqual('0', n[0]);
            Assert.True(RandomAccountNumberGenerator.IsValid(n));
        });
        // Aleatório (não sequencial): praticamente sem repetição em 2.000 amostras.
        Assert.True(numeros.Distinct().Count() >= 1_995);
    }

    [Theory]
    [InlineData("1000000016", true)]  // 100000001 + verificador 6
    [InlineData("1000000017", false)] // verificador errado
    [InlineData("100000001", false)]  // curto
    [InlineData("10000000a7", false)]
    public void NumeroDeConta_VerificadorDeLuhn(string numero, bool valido) =>
        Assert.Equal(valido, RandomAccountNumberGenerator.IsValid(numero));
}
