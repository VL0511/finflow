using FinFlow.Domain.Entities;

namespace FinFlow.Application.Common;

/// <summary>Perfis (roles) do sistema. Fonte única dos nomes usados nas policies e no seed.</summary>
public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Treasurer = "Treasurer";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Administrator, Treasurer, Customer];

    public static bool IsValid(string? role) => role is not null && All.Contains(role);
}

/// <summary>
/// Usuário da requisição atual. Na API vem do cookie de autenticação (claims emitidas pelo
/// servidor — nunca de dados enviados pelo frontend); no Worker é o "sistema" (não autenticado).
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}

/// <summary>
/// Escopo de dados do usuário — ÚNICO ponto que decide quais contas cada perfil enxerga.
/// - Administrator e Treasurer: todas as contas (operação da instituição);
/// - Customer: somente as contas das quais é dono;
/// - sem usuário (Worker/sistema): todas.
/// Para restringir o Treasurer a carteiras específicas no futuro, basta mudar aqui.
/// </summary>
public sealed record AccessScope(string? OwnerUserId)
{
    public static AccessScope Unrestricted { get; } = new((string?)null);

    public bool IsUnrestricted => OwnerUserId is null;

    public bool CanAccess(Account account) => IsUnrestricted || account.OwnerUserId == OwnerUserId;

    public static AccessScope For(ICurrentUser user)
    {
        if (!user.IsAuthenticated)
            return Unrestricted;

        if (user.IsInRole(Roles.Administrator) || user.IsInRole(Roles.Treasurer))
            return Unrestricted;

        // Customer (ou autenticado sem perfil reconhecido): só o que é dele. Sem id, nada.
        return new AccessScope(user.UserId ?? "\0sem-usuario");
    }
}

/// <summary>Usuário "sistema" (Worker, seed, testes): sem identidade, escopo irrestrito.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public string? UserId => null;
    public string? Email => null;
    public bool IsAuthenticated => false;
    public bool IsInRole(string role) => false;
}
