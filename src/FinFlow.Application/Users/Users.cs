using FinFlow.Application.Common;
using FluentValidation;

namespace FinFlow.Application.Users;

public sealed record UserResponse
{
    public string Id { get; init; } = string.Empty;

    /// <example>tesouraria@finflow.com</example>
    public string Email { get; init; } = string.Empty;

    /// <example>Maria Tesoureira</example>
    public string FullName { get; init; } = string.Empty;

    /// <summary>Administrator, Treasurer ou Customer.</summary>
    /// <example>Treasurer</example>
    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    /// <summary>A senha precisa ser trocada no próximo login.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>Bloqueado temporariamente por tentativas de login inválidas.</summary>
    public bool IsLockedOut { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateUserRequest
{
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;

    /// <summary>Senha inicial (política do Identity). Nunca registrada em logs ou auditoria.</summary>
    public string Password { get; init; } = string.Empty;

    public string Role { get; init; } = Roles.Customer;

    /// <summary>Exige troca da senha no primeiro login (recomendado para senhas definidas pelo administrador).</summary>
    public bool RequirePasswordChange { get; init; } = true;
}

public sealed record UpdateUserRequest
{
    public string? FullName { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record ResetPasswordRequest
{
    /// <summary>Nova senha temporária; o usuário deverá trocá-la no próximo login.</summary>
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record UserListQuery
{
    public string? Search { get; init; }
    public string? Role { get; init; }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("E-mail inválido.").MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Nome obrigatório.").MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Senha obrigatória.");
        RuleFor(x => x.Role).Must(Roles.IsValid).WithMessage($"Perfil inválido. Use: {string.Join(", ", Roles.All)}.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100).When(x => x.FullName is not null);
        RuleFor(x => x.Role).Must(Roles.IsValid).When(x => x.Role is not null)
            .WithMessage($"Perfil inválido. Use: {string.Join(", ", Roles.All)}.");
    }
}

/// <summary>Administração de usuários (somente Administrator). Implementada sobre o ASP.NET Core Identity.</summary>
public interface IUserManagementService
{
    Task<PagedResult<UserResponse>> ListAsync(UserListQuery query, PageRequest page, CancellationToken cancellationToken = default);
    Task<UserResponse> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ninguém altera o próprio perfil nem se desativa (evita autoelevação e bloqueio do último admin).</summary>
    Task<UserResponse> UpdateAsync(string id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(string id, ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Consultas mínimas sobre usuários usadas por outros módulos.</summary>
public interface IUserDirectory
{
    Task<bool> IsActiveUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}
