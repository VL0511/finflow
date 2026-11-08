using FluentValidation;

namespace FinFlow.Application.Transactions;

public sealed class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionRequestValidator()
    {
        RuleFor(x => x.SourceAccount)
            .NotEmpty().WithMessage("Conta de origem obrigatória.")
            .MaximumLength(20);

        RuleFor(x => x.DestinationAccount)
            .NotEmpty().WithMessage("Conta de destino obrigatória.")
            .MaximumLength(20)
            .NotEqual(x => x.SourceAccount).WithMessage("As contas devem ser diferentes.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("O valor deve ser maior que zero.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("O valor deve ter no máximo 2 casas decimais.");
    }
}
