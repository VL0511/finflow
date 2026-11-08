using Microsoft.AspNetCore.Identity;

namespace FinFlow.Infrastructure.Identity;

/// <summary>
/// Usuário do FinFlow sobre o ASP.NET Core Identity: hash de senha (PBKDF2), bloqueio por
/// tentativas, security stamp e roles vêm do framework — nada de gerenciamento de senha próprio.
/// </summary>
public sealed class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Desativado: não autentica e perde a sessão na próxima validação do cookie.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Precisa trocar a senha antes de usar o sistema (primeiro login, senha redefinida).</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
