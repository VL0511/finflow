using FluentValidation;

namespace FinFlow.Application.Accounts;

/// <summary>
/// Validação de formato. Existência/atividade da moeda e casas decimais por moeda são
/// conferidas no AccountService (dependem do cadastro de moedas).
/// </summary>
public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.HolderName)
            .NotEmpty().WithMessage("Titular obrigatório.")
            .MaximumLength(100);

        RuleFor(x => x.InitialBalance)
            .GreaterThanOrEqualTo(0).WithMessage("O saldo inicial não pode ser negativo.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("O saldo deve ter no máximo 2 casas decimais.");

        RuleFor(x => x.Currency)
            .Matches("^[A-Za-z]{3}$").WithMessage("Informe o código da moeda com 3 letras (ex.: BRL).");
    }
}
