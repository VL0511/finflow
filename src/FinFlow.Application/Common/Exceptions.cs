namespace FinFlow.Application.Common;

/// <summary>O recurso já existe (ex.: número de conta duplicado).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>
/// O registro foi alterado por outro processo entre a leitura e a gravação
/// (concorrência otimista via rowversion).
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Violação de índice/constraint UNIQUE no banco (ex.: dois cadastros simultâneos gerando o mesmo
/// número de conta, ou duas taxas vigentes para a mesma direção).
/// </summary>
public sealed class UniqueConstraintViolationException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Operação proibida para o usuário atual mesmo estando autenticado (HTTP 403).</summary>
public sealed class ForbiddenException(string message) : Exception(message);
