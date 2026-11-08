namespace FinFlow.Worker.Processing;

/// <summary>Política de novas tentativas ao processar uma transação (seção Worker:Processing).</summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Worker:Processing";

    /// <summary>
    /// Tentativas para falhas transitórias (conflito de concorrência, deadlock, timeout, banco
    /// indisponível). Esgotadas, a mensagem VOLTA para a fila — nunca vai para a DLQ.
    /// </summary>
    public int MaxTransientAttempts { get; set; } = 6;

    /// <summary>Tentativas para falhas não transitórias. Esgotadas, a mensagem vai para a DLQ.</summary>
    public int MaxPersistentAttempts { get; set; } = 3;

    /// <summary>Espera base do backoff exponencial (dobra a cada tentativa, com jitter).</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Teto de espera entre tentativas.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Consumo da fila de transferências (seção Worker:Consumer).</summary>
public sealed class ConsumerOptions
{
    public const string SectionName = "Worker:Consumer";

    /// <summary>
    /// Mensagens processadas em paralelo por instância do Worker. A segurança financeira não depende
    /// deste valor (o handler bloqueia transação e contas com UPDLOCK); ele limita o uso de conexões
    /// do SQL Server e a disputa por contas muito movimentadas. Cada mensagem usa uma conexão.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Mensagens entregues e ainda sem ACK por consumidor. Sempre ≥ MaxConcurrency.</summary>
    public int PrefetchCount { get; set; } = 16;

    /// <summary>Espera entre tentativas de (re)iniciar o consumo após falha de conexão/canal.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Recuperação de transações presas em Processing ou Pending (seção Worker:Recovery).</summary>
public sealed class RecoveryOptions
{
    public const string SectionName = "Worker:Recovery";

    public bool Enabled { get; set; } = true;

    /// <summary>Intervalo entre varreduras (a primeira roda na inicialização).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Há quanto tempo a transação precisa estar em Processing para ser considerada presa.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Há quanto tempo (desde CreatedAt) a transação precisa estar em Pending para ser considerada
    /// presa (mensagem perdida ou enviada à DLQ antes da etapa 1). Bem acima da latência normal
    /// do outbox + fila, para não disputar à toa com o consumer; também absorve diferença de
    /// relógio entre API e Worker.
    /// </summary>
    public TimeSpan PendingStaleAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Máximo de transações retomadas por varredura, POR STATUS (até BatchSize em Processing e
    /// depois até BatchSize em Pending). Orçamentos separados: transações com falha permanente
    /// em um status não impedem a recuperação do outro.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
