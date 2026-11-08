using FinFlow.Domain.Entities;
using FinFlow.Domain.Exceptions;
using FinFlow.UnitTests.Mocks;

namespace FinFlow.UnitTests.Domain;

public class AccountTests
{
    [Fact]
    public void Criar_ComDadosValidos_DeveNormalizarMoedaEIniciarAtiva()
    {
        var account = new Account(" 12345 ", " Ana Souza ", 1_000.00m, "brl");

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("12345", account.AccountNumber);
        Assert.Equal("Ana Souza", account.HolderName);
        Assert.Equal(1_000.00m, account.Balance);
        Assert.Equal("BRL", account.Currency);
        Assert.True(account.IsActive);
    }

    [Theory]
    [InlineData("", "Ana", 0, "BRL", "Número da conta obrigatório.")]
    [InlineData("123", "", 0, "BRL", "Titular obrigatório.")]
    [InlineData("123", "Ana", -1, "BRL", "O saldo inicial não pode ser negativo.")]
    [InlineData("123", "Ana", 0, "REAL", "Moeda deve ter 3 letras (ex.: BRL).")]
    public void Criar_ComDadosInvalidos_DeveLancarArgumentException(
        string numero, string titular, decimal saldo, string moeda, string mensagem)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Account(numero, titular, saldo, moeda));

        Assert.Equal(mensagem, ex.Message);
    }

    [Fact]
    public void Debitar_ComSaldo_DeveReduzirSaldo()
    {
        var ana = TransactionMockData.Ana(); // R$ 1.000

        ana.Debit(400.00m);

        Assert.Equal(600.00m, ana.Balance);
    }

    [Fact]
    public void Debitar_SaldoExato_DeveZerarConta()
    {
        var bruno = TransactionMockData.Bruno(); // R$ 250

        bruno.Debit(250.00m);

        Assert.Equal(0m, bruno.Balance);
    }

    [Fact]
    public void Debitar_SemSaldo_DeveLancarDomainExceptionEManterSaldo()
    {
        var bruno = TransactionMockData.Bruno(); // R$ 250

        var ex = Assert.Throws<DomainException>(() => bruno.Debit(250.01m));

        Assert.Equal("Saldo insuficiente.", ex.Message);
        Assert.Equal(250.00m, bruno.Balance);
    }

    [Fact]
    public void Debitar_ContaInativa_DeveLancarDomainException()
    {
        var diego = TransactionMockData.Diego();

        Assert.Throws<DomainException>(() => diego.Debit(10m));
        Assert.Equal(300.00m, diego.Balance);
    }

    [Fact]
    public void Creditar_DeveAumentarSaldo()
    {
        var bruno = TransactionMockData.Bruno();

        bruno.Credit(500.00m);

        Assert.Equal(750.00m, bruno.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void DebitarOuCreditar_ValorNaoPositivo_DeveLancarArgumentException(decimal valor)
    {
        var ana = TransactionMockData.Ana();

        Assert.Throws<ArgumentException>(() => ana.Debit(valor));
        Assert.Throws<ArgumentException>(() => ana.Credit(valor));
    }

    [Fact]
    public void DesativarEReativar_DeveAlternarSituacao()
    {
        var ana = TransactionMockData.Ana();

        ana.Deactivate();
        Assert.False(ana.IsActive);

        ana.Activate();
        Assert.True(ana.IsActive);
    }
}
