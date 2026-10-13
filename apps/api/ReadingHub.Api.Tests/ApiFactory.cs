using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ReadingHub.Api.Data;
using Testcontainers.PostgreSql;

namespace ReadingHub.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", _postgres.GetConnectionString());
        builder.UseSetting("Auth:JwtSigningKey", "test-signing-key-at-least-32-characters");
        builder.UseSetting("Auth:LoginPermitLimit", "1000");
        builder.UseSetting("Cors:WebOrigin", "http://localhost:5173");
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(Path.GetTempPath(), "readinghub-tests", Guid.NewGuid().ToString("N")));
    }

    public HttpClient CreateCookieClient()
    {
        var handler = new CookieHandler { InnerHandler = Server.CreateHandler() };
        return new HttpClient(handler) { BaseAddress = Server.BaseAddress };
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "Users" CASCADE""");
    }

    public async Task<HttpClient> RegisterAsync(string email = "reader@example.com", string password = "correct-horse")
    {
        var client = CreateCookieClient();
        var csrf = await CsrfAsync(client);
        var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/register", new { email, password, displayName = "Reader" }, csrf);
        response.EnsureSuccessStatusCode();
        return client;
    }

    public async Task<string> CsrfAsync(HttpClient client)
    {
        var payload = await client.GetFromJsonAsync<CsrfBody>("/api/v1/auth/csrf", Json);
        return payload!.Token;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body, string? csrf = null)
    {
        var message = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: Json);
        }

        if (csrf is not null)
        {
            message.Headers.Add("X-CSRF-TOKEN", csrf);
        }

        return await client.SendAsync(message);
    }

    private sealed record CsrfBody(string Token);
}

public sealed class CookieHandler : DelegatingHandler
{
    private readonly CookieContainer _cookies = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var header = _cookies.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                _cookies.SetCookies(request.RequestUri!, value);
            }
        }

        return response;
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
