using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;

namespace FinFlow.IntegrationTests.Infrastructure;

public static class HttpHelpers
{
    public static async Task<TransactionResponse> CreateTransactionAsync(
        this HttpClient client, string source, string destination, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            sourceAccount = source,
            destinationAccount = destination,
            amount
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>())!;
    }

    /// <summary>Consulta o status até chegar a um estado final (ou estourar o tempo).</summary>
    public static async Task<TransactionStatusResponse> WaitForFinalStatusAsync(
        this HttpClient client, Guid transactionId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (true)
        {
            var status = await client.GetFromJsonAsync<TransactionStatusResponse>(
                $"/api/transactions/{transactionId}/status");

            if (status!.Status is "Completed" or "Failed" or "Cancelled")
                return status;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"Transação {transactionId} não finalizou a tempo (último status: {status.Status}).");

            await Task.Delay(200);
        }
    }

    /// <summary>Percorre todas as páginas de uma listagem paginada (pageSize máximo).</summary>
    public static async Task<List<T>> GetAllPagesAsync<T>(this HttpClient client, string path)
    {
        var separator = path.Contains('?') ? '&' : '?';
        var all = new List<T>();
        for (var page = 1; ; page++)
        {
            var result = await client.GetFromJsonAsync<PagedResult<T>>(
                $"{path}{separator}page={page}&pageSize={PageRequest.MaxPageSize}");
            all.AddRange(result!.Items);
            if (!result.HasNextPage)
                return all;
        }
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());
}
