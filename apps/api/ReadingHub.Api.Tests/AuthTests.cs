using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReadingHub.Api.Data;
using ReadingHub.Api.Domain;
using ReadingHub.Api.Infrastructure;

namespace ReadingHub.Api.Tests;

[Collection("api")]
public sealed class AuthTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task First_user_can_register_and_second_registration_is_rejected()
    {
        var client = await factory.RegisterAsync();
        var me = await client.GetFromJsonAsync<UserBody>("/api/v1/auth/me", factory.Json);
        Assert.Equal("reader@example.com", me!.Email);

        var other = factory.CreateCookieClient();
        var csrf = await factory.CsrfAsync(other);
        var response = await factory.SendAsync(other, HttpMethod.Post, "/api/v1/auth/register", new { email = "other@example.com", password = "another-password", displayName = "Other" }, csrf);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_logout_ends_the_session()
    {
        await factory.RegisterAsync();
        var client = factory.CreateCookieClient();
        var csrf = await factory.CsrfAsync(client);
        var login = await factory.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { email = "reader@example.com", password = "wrong-password" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        csrf = await factory.CsrfAsync(client);
        login = await factory.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { email = "reader@example.com", password = "correct-horse" }, csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        csrf = await factory.CsrfAsync(client);
        var logout = await factory.SendAsync(client, HttpMethod.Post, "/api/v1/auth/logout", new { }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var client = factory.CreateCookieClient();
        var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Seeded_second_user_cannot_read_the_first_users_settings()
    {
        var first = await factory.RegisterAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "second@example.com",
            PasswordHash = hasher.Hash("second-password"),
            DisplayName = "Second",
            SecurityStamp = TokenService.NewSecret(),
            CreatedAt = now,
            UpdatedAt = now,
            Settings = new UserSettings()
        });
        await db.SaveChangesAsync();

        var second = factory.CreateCookieClient();
        var csrf = await factory.CsrfAsync(second);
        var login = await factory.SendAsync(second, HttpMethod.Post, "/api/v1/auth/login", new { email = "second@example.com", password = "second-password" }, csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var firstSettings = await first.GetFromJsonAsync<SettingsBody>("/api/v1/settings", factory.Json);
        var secondSettings = await second.GetFromJsonAsync<SettingsBody>("/api/v1/settings", factory.Json);
        Assert.NotNull(firstSettings);
        Assert.NotNull(secondSettings);
        Assert.Equal("en", secondSettings!.Locale);
    }

    private sealed record UserBody(Guid Id, string Email, string DisplayName);
    private sealed record SettingsBody(string Locale);
}
