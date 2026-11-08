namespace FinFlow.Application.Common;

/// <summary>
/// Trilha de auditoria. O registro entra no MESMO SaveChanges/commit da operação auditada:
/// se a operação for desfeita, o registro também é (nada é auditado sem ter acontecido).
/// O responsável (usuário atual) é preenchido pela implementação.
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Adiciona um registro à unidade de trabalho atual. <c>details</c> é serializado como JSON:
    /// nunca passe senhas, tokens ou hashes.
    /// </summary>
    void Record(string action, string entityType, string? entityId, object? details = null, string result = AuditResults.Succeeded);

    /// <summary>Como <see cref="Record"/>, com o responsável explícito (ex.: login, antes de haver sessão).</summary>
    void RecordFor(string? userId, string? userEmail, string action, string entityType, string? entityId, object? details = null, string result = AuditResults.Succeeded);
}

public static class AuditResults
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

/// <summary>Tipos de operação auditados.</summary>
public static class AuditActions
{
    // Financeiras
    public const string TransferRequested = "TransferRequested";
    public const string TransferCompleted = "TransferCompleted";
    public const string TransferFailed = "TransferFailed";
    public const string TransferCancelled = "TransferCancelled";
    public const string AccountCreated = "AccountCreated";

    // Moedas e câmbio
    public const string CurrencyCreated = "CurrencyCreated";
    public const string CurrencyUpdated = "CurrencyUpdated";
    public const string CurrencyActivated = "CurrencyActivated";
    public const string CurrencyDeactivated = "CurrencyDeactivated";
    public const string ExchangeRateSet = "ExchangeRateSet";
    public const string ExchangeRateDeactivated = "ExchangeRateDeactivated";

    // Usuários e acesso
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserActivated = "UserActivated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserRoleChanged = "UserRoleChanged";
    public const string UserPasswordReset = "UserPasswordReset";
    public const string PasswordChanged = "PasswordChanged";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
}
