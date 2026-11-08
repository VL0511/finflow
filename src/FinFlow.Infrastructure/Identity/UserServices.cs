using FinFlow.Application.Common;
using FinFlow.Application.Users;
using FinFlow.Infrastructure.Persistence.Context;
using FinFlow.Infrastructure.Persistence.Repositories;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Identity;

public sealed class UserDirectory(FinFlowDbContext context) : IUserDirectory
{
    public Task<bool> IsActiveUserAsync(string userId, CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<string, string>();

        var ids = userIds.ToList();
        return await context.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? string.Empty, cancellationToken);
    }
}

/// <summary>
/// Administração de usuários sobre o UserManager do Identity. Cada operação e seu registro de
/// auditoria são gravados na mesma transação SQL. Alterar perfil, desativar ou redefinir senha
/// renova o security stamp: as sessões abertas do usuário deixam de valer.
/// </summary>
public sealed class UserManagementService(
    UserManager<AppUser> userManager,
    FinFlowDbContext context,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAuditTrail audit,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator) : IUserManagementService
{
    public async Task<PagedResult<UserResponse>> ListAsync(UserListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var users = Projection();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u => u.Email.Contains(term) || u.FullName.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.Role))
            users = users.Where(u => u.Role == query.Role);

        var result = await users.OrderBy(u => u.Email).ThenBy(u => u.Id).ToPagedResultAsync(page, cancellationToken);
        return result.Map(ToResponse);
    }

    public async Task<UserResponse> GetAsync(string id, CancellationToken cancellationToken = default) =>
        ToResponse(await Projection().FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Usuário '{id}' não encontrado."));

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var email = request.Email.Trim();

        string? id = null;
        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            if (await userManager.FindByEmailAsync(email) is not null)
                throw new ConflictException($"Já existe um usuário com o e-mail '{email}'.");

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = request.FullName.Trim(),
                MustChangePassword = request.RequirePasswordChange
            };
            Ensure(await userManager.CreateAsync(user, request.Password), nameof(request.Password));
            Ensure(await userManager.AddToRoleAsync(user, request.Role), nameof(request.Role));

            audit.Record(AuditActions.UserCreated, "User", user.Id,
                new { user.Email, user.FullName, request.Role, request.RequirePasswordChange });
            id = user.Id;
        }, cancellationToken);

        return await GetAsync(id!, cancellationToken);
    }

    public async Task<UserResponse> UpdateAsync(string id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var isSelf = id == currentUser.UserId;

        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            var user = await userManager.FindByIdAsync(id) ?? throw new NotFoundException($"Usuário '{id}' não encontrado.");
            var currentRole = (await userManager.GetRolesAsync(user)).FirstOrDefault();
            var invalidateSessions = false;

            if (request.Role is { } role && role != currentRole)
            {
                // Ninguém altera o próprio perfil: impede autoelevação e o admin rebaixar a si mesmo.
                if (isSelf)
                    throw new ForbiddenException("Você não pode alterar o seu próprio perfil.");

                if (currentRole is not null)
                    Ensure(await userManager.RemoveFromRoleAsync(user, currentRole), nameof(request.Role));
                Ensure(await userManager.AddToRoleAsync(user, role), nameof(request.Role));
                audit.Record(AuditActions.UserRoleChanged, "User", user.Id, new { user.Email, from = currentRole, to = role });
                invalidateSessions = true;
            }

            if (request.IsActive is { } active && active != user.IsActive)
            {
                if (isSelf && !active)
                    throw new ForbiddenException("Você não pode desativar a si mesmo.");

                user.IsActive = active;
                audit.Record(active ? AuditActions.UserActivated : AuditActions.UserDeactivated, "User", user.Id, new { user.Email });
                invalidateSessions |= !active;
            }

            if (request.FullName is { } name && name.Trim() != user.FullName)
            {
                audit.Record(AuditActions.UserUpdated, "User", user.Id,
                    new { user.Email, FullName = new { before = user.FullName, after = name.Trim() } });
                user.FullName = name.Trim();
            }

            Ensure(await userManager.UpdateAsync(user), nameof(request.FullName));
            if (invalidateSessions)
                Ensure(await userManager.UpdateSecurityStampAsync(user), "SecurityStamp");
        }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(string id, ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
            throw new ValidationException([new ValidationFailure(nameof(request.NewPassword), "Senha obrigatória.")]);

        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            var user = await userManager.FindByIdAsync(id) ?? throw new NotFoundException($"Usuário '{id}' não encontrado.");

            // Valida a nova senha ANTES de remover a atual (falha não deixa o usuário sem senha).
            foreach (var validator in userManager.PasswordValidators)
                Ensure(await validator.ValidateAsync(userManager, user, request.NewPassword), nameof(request.NewPassword));

            Ensure(await userManager.RemovePasswordAsync(user), nameof(request.NewPassword));
            Ensure(await userManager.AddPasswordAsync(user, request.NewPassword), nameof(request.NewPassword));
            user.MustChangePassword = true;
            await userManager.SetLockoutEndDateAsync(user, null);
            Ensure(await userManager.UpdateAsync(user), nameof(request.NewPassword));
            Ensure(await userManager.UpdateSecurityStampAsync(user), "SecurityStamp");

            // Nunca a senha: só o fato.
            audit.Record(AuditActions.UserPasswordReset, "User", user.Id, new { user.Email });
        }, cancellationToken);
    }

    // Inicializador de objeto (não construtor): o EF consegue traduzir Where/OrderBy sobre a projeção.
    private sealed class UserRow
    {
        public string Id { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string? Role { get; init; }
        public bool IsActive { get; init; }
        public bool MustChangePassword { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    /// <summary>Usuário + perfil (um por usuário) numa única consulta.</summary>
    private IQueryable<UserRow> Projection() =>
        from u in context.Users.AsNoTracking()
        select new UserRow
        {
            Id = u.Id,
            Email = u.Email!,
            FullName = u.FullName,
            Role = (from ur in context.UserRoles
                    join r in context.Roles on ur.RoleId equals r.Id
                    where ur.UserId == u.Id
                    select r.Name).FirstOrDefault(),
            IsActive = u.IsActive,
            MustChangePassword = u.MustChangePassword,
            LockoutEnd = u.LockoutEnd,
            CreatedAt = u.CreatedAt
        };

    private static UserResponse ToResponse(UserRow u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        Role = u.Role ?? string.Empty,
        IsActive = u.IsActive,
        MustChangePassword = u.MustChangePassword,
        IsLockedOut = u.LockoutEnd is { } end && end > DateTimeOffset.UtcNow,
        CreatedAt = u.CreatedAt
    };

    /// <summary>Erros do Identity (senha fraca, e-mail inválido...) viram 400 com mensagens por campo.</summary>
    private static void Ensure(IdentityResult result, string property)
    {
        if (result.Succeeded)
            return;
        if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
            throw new ConflictException("Já existe um usuário com este e-mail.");
        throw new ValidationException(result.Errors.Select(e => new ValidationFailure(property, e.Description)));
    }
}
