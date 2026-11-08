using FinFlow.Application.Transactions;
using FinFlow.UnitTests.Mocks;

namespace FinFlow.UnitTests.Application;

public class CreateTransactionRequestValidatorTests
{
    private readonly CreateTransactionRequestValidator _validator = new();

    [Fact]
    public void Validar_RequestValido_NaoDeveTerErros()
    {
        var result = _validator.Validate(TransactionMockData.RequestValido());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(500.00)]
    [InlineData(999999.99)]
    public void Validar_ValoresPositivosComAteDuasCasas_DeveSerValido(decimal valor)
    {
        var request = TransactionMockData.RequestValido() with { Amount = valor };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0, "O valor deve ser maior que zero.")]
    [InlineData(-10, "O valor deve ser maior que zero.")]
    [InlineData(10.555, "O valor deve ter no máximo 2 casas decimais.")]
    public void Validar_ValorInvalido_DeveRetornarErroNoAmount(decimal valor, string mensagem)
    {
        var request = TransactionMockData.RequestValido() with { Amount = valor };

        var result = _validator.Validate(request);

        var erro = Assert.Single(result.Errors);
        Assert.Equal(nameof(CreateTransactionRequest.Amount), erro.PropertyName);
        Assert.Equal(mensagem, erro.ErrorMessage);
    }

    [Fact]
    public void Validar_ContasIguais_DeveRetornarErro()
    {
        var request = TransactionMockData.RequestValido() with { DestinationAccount = TransactionMockData.ContaAna };

        var result = _validator.Validate(request);

        Assert.Contains(result.Errors, e => e.ErrorMessage == "As contas devem ser diferentes.");
    }

    [Fact]
    public void Validar_ContasVazias_DeveRetornarErroEmAmbas()
    {
        var request = new CreateTransactionRequest { SourceAccount = "", DestinationAccount = "", Amount = 10 };

        var result = _validator.Validate(request);

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Conta de origem obrigatória.");
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Conta de destino obrigatória.");
    }

    [Fact]
    public void Validar_ContaComMaisDe20Caracteres_DeveRetornarErro()
    {
        var request = TransactionMockData.RequestValido() with { SourceAccount = new string('9', 21) };

        var result = _validator.Validate(request);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTransactionRequest.SourceAccount));
    }
}
