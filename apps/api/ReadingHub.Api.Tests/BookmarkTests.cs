using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ReadingHub.Api.Tests;

[Collection("api")]
public sealed class BookmarkTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creates_searches_and_filters_bookmarks()
    {
        var client = await factory.RegisterAsync();
        var csrf = await factory.CsrfAsync(client);
        var collection = await factory.SendAsync(client, HttpMethod.Post, "/api/v1/collections", new { name = "Delphi", color = "#336699", isPinned = true, sortOrder = 0 }, csrf);
        Assert.Equal(HttpStatusCode.Created, collection.StatusCode);
        var collectionId = (await collection.Content.ReadFromJsonAsync<JsonElement>(factory.Json)).GetProperty("id").GetGuid();

        csrf = await factory.CsrfAsync(client);
        var created = await factory.SendAsync(client, HttpMethod.Post, "/api/v1/bookmarks", new
        {
            url = "https://Example.com/delphi?utm_source=test",
            title = "Delphi notes",
            fetchMetadata = false,
            collectionIds = new[] { collectionId },
            tags = new[] { "programming" }
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/bookmarks?q=delphi&tag=programming&collectionId=" + collectionId, factory.Json);
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
        Assert.Equal("https://example.com/delphi", list.GetProperty("items")[0].GetProperty("normalizedUrl").GetString());

        var empty = await client.GetFromJsonAsync<JsonElement>("/api/v1/bookmarks?q=missing", factory.Json);
        Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Duplicate_urls_conflict_and_other_users_cannot_read_them()
    {
        var owner = await factory.RegisterAsync();
        var csrf = await factory.CsrfAsync(owner);
        var created = await factory.SendAsync(owner, HttpMethod.Post, "/api/v1/bookmarks", new { url = "https://example.com/a", title = "A", fetchMetadata = false }, csrf);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(factory.Json)).GetProperty("id").GetGuid();
        csrf = await factory.CsrfAsync(owner);
        var duplicate = await factory.SendAsync(owner, HttpMethod.Post, "/api/v1/bookmarks", new { url = "https://example.com/a/", fetchMetadata = false }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadingHub.Api.Data.AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<ReadingHub.Api.Infrastructure.PasswordHasher>();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new ReadingHub.Api.Domain.User
        {
            Id = userId,
            Email = "second@example.com",
            PasswordHash = hasher.Hash("second-password"),
            DisplayName = "Second",
            SecurityStamp = ReadingHub.Api.Infrastructure.TokenService.NewSecret(),
            CreatedAt = now,
            UpdatedAt = now,
            Settings = new ReadingHub.Api.Domain.UserSettings { UserId = userId }
        });
        await db.SaveChangesAsync();

        var other = factory.CreateCookieClient();
        csrf = await factory.CsrfAsync(other);
        var login = await factory.SendAsync(other, HttpMethod.Post, "/api/v1/auth/login", new { email = "second@example.com", password = "second-password" }, csrf);
        login.EnsureSuccessStatusCode();
        var hidden = await other.GetAsync($"/api/v1/bookmarks/{id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var list = await other.GetFromJsonAsync<JsonElement>("/api/v1/bookmarks", factory.Json);
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
    }
}
