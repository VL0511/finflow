using FinFlow.API.Middleware;
using FinFlow.API.Security;
using FinFlow.Application;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Identity;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFinFlowSecurity(builder.Configuration);
builder.Services.AddOutboxPublisher();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// CORS: só as origens listadas em Cors:AllowedOrigins (em dev, o Vite). Sem a chave, nenhuma origem é liberada.
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials() // cookie de sessão quando o frontend usa outra origem (VITE_API_BASE_URL)
        .WithExposedHeaders("Location")));

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FinFlow API",
        Version = "v1",
        Description = """
            API de transações financeiras do FinFlow.

            ### Fluxo de uma transferência
            1. `POST /api/transactions` registra a transação como **Pending** e publica `TransactionRequested` no RabbitMQ (via outbox).
            2. O **Worker** consome a mensagem e muda para **Processing**.
            3. O Worker valida as contas (existência, situação, saldo e moeda).
            4. Débito, crédito e status final são gravados numa única transação SQL: **Completed** ou **Failed** (com motivo).

            ### Ciclo de vida do status
            | Transição | Quem faz |
            |---|---|
            | Pending → Processing | Worker |
            | Processing → Completed | Worker, no mesmo commit do débito e crédito |
            | Processing → Failed | Worker, registrando o motivo |
            | Pending → Cancelled | Cliente, via `PATCH /api/transactions/{id}/cancel` |

            Não existe endpoint para alterar o status livremente: o status precisa refletir o que de fato
            aconteceu com os saldos, e é ele que garante a idempotência do Worker.

            ### Autenticação
            Faça `POST /api/auth/login` (o Swagger guarda o cookie de sessão). Em desenvolvimento o
            administrador inicial é **admin@finflow.com / admin**, com troca de senha obrigatória
            (`POST /api/auth/change-password`) antes de usar o restante da API.

            | Perfil | Pode |
            |---|---|
            | Administrator | tudo, inclusive usuários, perfis, moedas, taxas e auditoria |
            | Treasurer | consultar todas as contas e transações, transferir, consultar taxas vigentes |
            | Customer | abrir e consultar as próprias contas, transferir a partir delas, ver o próprio histórico |

            ### Câmbio
            Transferências entre moedas diferentes usam a taxa vigente no momento da criação; valor
            convertido e taxa ficam gravados na transação e não mudam se a taxa for alterada depois.

            ### Contas fictícias disponíveis
            | Conta | Titular | Saldo | Moeda | Situação |
            |---|---|---|---|---|
            | 12345 | Ana Souza | 1.000,00 | BRL | Ativa |
            | 67890 | Bruno Lima | 250,00 | BRL | Ativa |
            | 11223 | Carla Mendes | 5.000,00 | USD | Ativa |
            | 44556 | Diego Rocha | 300,00 | BRL | Inativa |
            """
    });

    foreach (var xml in new[] { "FinFlow.API.xml", "FinFlow.Application.xml" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(path))
            options.IncludeXmlComments(path);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FinFlow API v1");
        options.DocumentTitle = "FinFlow API";
    });
}

// Em dev, o redirect 307 http→https (porta 7086) quebra o proxy do Vite e dispara erro de CORS no navegador.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// Roles e administrador inicial (idempotente). Não aplica migrations: o banco precisa estar atualizado.
try
{
    await IdentitySeeder.SeedAsync(app.Services);
}
catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) // Invalid object name
{
    throw new InvalidOperationException(
        "O banco de dados não está atualizado. Aplique as migrations antes de iniciar a API " +
        "(dotnet ef database update --project src/FinFlow.Infrastructure --startup-project src/FinFlow.API).", ex);
}

app.Run();

// Expõe a classe Program para o WebApplicationFactory dos testes de integração.
public partial class Program;
