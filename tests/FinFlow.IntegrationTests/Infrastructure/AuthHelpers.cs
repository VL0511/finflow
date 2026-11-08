using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinFlow.IntegrationTests.Infrastructure;

public static class AuthHelpers
{
    /// <summary>
    /// Cliente com o cookie de sessão (o HttpClient da factory guarda cookies). Se
    /// <paramref name="newPassword"/> for informado, faz a troca de senha do primeiro login.
    /// </summary>
    public static async Task<HttpClient> LoginAsync(
        this WebApplicationFactory<Program> factory, string email, string password, string? newPassword = null)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        if (!login.IsSuccessStatusCode)
            throw new InvalidOperationException($"Login de {email} falhou: {(int)login.StatusCode} {await login.Content.ReadAsStringAsync()}");

        if (newPassword is not null)
        {
            var change = await client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = password, newPassword });
            change.EnsureSuccessStatusCode();
        }
        return client;
    }
}
