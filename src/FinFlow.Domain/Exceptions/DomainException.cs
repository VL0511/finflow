namespace FinFlow.Domain.Exceptions;

/// <summary>Violação de uma regra de negócio.</summary>
public sealed class DomainException(string message) : Exception(message);
