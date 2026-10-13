using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReadingHub.Api.Data;

namespace ReadingHub.Api.Security;

public sealed class SessionAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "session";
    public const string CookieName = "rh_session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var raw) || !Guid.TryParse(raw, out var sessionId))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await db.WebSessions.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == sessionId);
        var now = DateTimeOffset.UtcNow;
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            return AuthenticateResult.Fail("Session expired.");
        }

        if (now - session.LastSeenAt > TimeSpan.FromMinutes(5))
        {
            var idleDays = configuration.GetValue("Auth:SessionIdleDays", 14);
            var absoluteDays = configuration.GetValue("Auth:SessionAbsoluteDays", 30);
            var idleExpiry = now.AddDays(idleDays);
            var absoluteExpiry = session.CreatedAt.AddDays(absoluteDays);
            session.LastSeenAt = now;
            session.ExpiresAt = idleExpiry < absoluteExpiry ? idleExpiry : absoluteExpiry;
            await db.SaveChangesAsync();
        }

        var claims = new[]
        {
            new Claim("uid", session.UserId.ToString()),
            new Claim("sid", session.Id.ToString()),
            new Claim("stamp", session.User.SecurityStamp),
            new Claim("amr", "cookie")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class CurrentUser
{
    public static Guid Id(HttpContext httpContext)
    {
        var value = httpContext.User.FindFirst("uid")?.Value;
        if (!Guid.TryParse(value, out var id))
        {
            throw new Infrastructure.AppException("Unauthorized", StatusCodes.Status401Unauthorized, "Authentication is required.");
        }

        return id;
    }

    public static Guid? SessionId(HttpContext httpContext)
    {
        var value = httpContext.User.FindFirst("sid")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static bool IsCookie(HttpContext httpContext) =>
        httpContext.User.FindFirst("amr")?.Value == "cookie";
}
