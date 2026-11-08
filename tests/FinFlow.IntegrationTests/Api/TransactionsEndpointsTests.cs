using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Messaging;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Enums;
using FinFlow.IntegrationTests.Infrastructure;
using FinFlow.Infrastructure.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests.Api;

/// <summary>
/// Endpoints de transação com SQL Server real. O publicador do outbox está
/// desligado aqui, então nada é processado: dá para verificar o estado Pending
/// e o que foi gravado no outbox.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TransactionsEndpointsTests(IntegrationTestEnvironment env)
{
    private readonly HttpClient _client = env.AdminClient;

    [Fact]
    public async Task Post_Valido_Retorna202Pending_EGravaTransacaoEEventoNoOutbox()
    {
        var origem = await env.CreateAccountAsync(1000m);
        var destino = await env.CreateAccountAsync(250m);

        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            sourceAccount = origem.AccountNumber,
            destinationAccount = destino.AccountNumber,
            amount = 500.00m
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.Equal("Pending", body!.Status);
        Assert.Equal("BRL", body.Currency);
        Assert.EndsWith($"/api/transactions/{body.Id}/status", response.Headers.Location!.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);

        await using var db = env.CreateDbContext();

        var transacao = await db.Transactions.SingleAsync(t => t.Id == body.Id);
        Assert.Equal(TransactionStatus.Pending, transacao.Status);
        Assert.Equal(500.00m, transacao.Amount);

        // Outbox: o evento foi gravado junto com a transação e ainda não foi publicado.
        var mensagens = await db.OutboxMessages.Where(m => m.Payload.Contains(body.Id.ToString())).ToListAsync();
        var mensagem = Assert.Single(mensagens);
        Assert.Equal(TransactionRequested.MessageType, mensagem.Type);
        Assert.Null(mensagem.ProcessedAt);

        var evento = JsonSerializer.Deserialize<TransactionRequested>(mensagem.Payload, MessagingJson.Options);
        Assert.Equal(body.Id, evento!.TransactionId);
        Assert.Equal(500.00m, evento.Amount);

        // Saldos só mudam quando o Worker processar.
        Assert.Equal(1000m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(250m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task Post_MoedaDaTransacao_VemDaContaDeOrigem()
    {
        var origem = await env.CreateAccountAsync(1000m, "USD");
        var destino = await env.CreateAccountAsync(0m, "USD");

        var body = await _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 10m);

        Assert.Equal("USD", body.Currency);
    }

    [Fact]
    public async Task Post_ContaInexistente_Retorna422_ENaoGravaNada()
    {
        var destino = await env.CreateAccountAsync(0m);
        var origemInexistente = IntegrationTestEnvironment.NewAccountNumber();

        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            sourceAccount = origemInexistente,
            destinationAccount = destino.AccountNumber,
            amount = 10m
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Conta de origem não encontrada.", (await response.ReadProblemAsync()).GetProperty("detail").GetString());

        await using var db = env.CreateDbContext();
        Assert.False(await db.Transactions.AnyAsync(t => t.SourceAccount == origemInexistente));
        Assert.False(await db.OutboxMessages.AnyAsync(m => m.Payload.Contains(origemInexistente)));
    }

    [Theory]
    [InlineData("111", "111", 10, "DestinationAccount")]
    [InlineData("111", "222", 0, "Amount")]
    [InlineData("111", "222", 10.555, "Amount")]
    [InlineData("", "222", 10, "SourceAccount")]
    public async Task Post_DadosInvalidos_Retorna400(string origem, string destino, decimal valor, string campo)
    {
        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            sourceAccount = origem,
            destinationAccount = destino,
            amount = valor
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadProblemAsync()).GetProperty("errors").TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task GetById_GetStatus_EList_RetornamATransacao()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var criada = await _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 42.50m);

        var porId = await _client.GetFromJsonAsync<TransactionResponse>($"/api/transactions/{criada.Id}");
        var status = await _client.GetFromJsonAsync<TransactionStatusResponse>($"/api/transactions/{criada.Id}/status");
        var lista = (await _client.GetFromJsonAsync<PagedResult<TransactionResponse>>(
            $"/api/transactions?accountNumber={origem.AccountNumber}"))!.Items;

        Assert.Equal(42.50m, porId!.Amount);
        Assert.Equal("Pending", status!.Status);
        Assert.Null(status.FailureReason);
        Assert.Contains(lista!, t => t.Id == criada.Id);
    }

    [Fact]
    public async Task Cancel_TransacaoPendente_Retorna200EPersisteCancelled()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var criada = await _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 10m);

        var response = await _client.PatchAsync($"/api/transactions/{criada.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var salva = await env.GetTransactionAsync(criada.Id);
        Assert.Equal(TransactionStatus.Cancelled, salva!.Status);
        Assert.NotNull(salva.UpdatedAt);
    }

    [Fact]
    public async Task Cancel_DuasVezes_SegundaRetorna409()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var criada = await _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 10m);

        await _client.PatchAsync($"/api/transactions/{criada.Id}/cancel", null);
        var segunda = await _client.PatchAsync($"/api/transactions/{criada.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("Somente transações pendentes podem ser canceladas.",
            (await segunda.ReadProblemAsync()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/transactions/{0}")]
    [InlineData("GET", "/api/transactions/{0}/status")]
    [InlineData("PATCH", "/api/transactions/{0}/cancel")]
    public async Task TransacaoInexistente_Retorna404(string metodo, string rota)
    {
        var request = new HttpRequestMessage(new HttpMethod(metodo), string.Format(rota, Guid.NewGuid()));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
