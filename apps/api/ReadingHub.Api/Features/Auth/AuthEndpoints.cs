using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ReadingHub.Api.Data;
using ReadingHub.Api.Domain;
using ReadingHub.Api.Infrastructure;
using ReadingHub.Api.Security;

namespace ReadingHub.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record DeviceCodeExchangeRequest(string Code, string DeviceName);
public sealed record RefreshRequest(string RefreshToken);
public sealed record UserResponse(Guid Id, string Email, string DisplayName);
public sealed record AuthResponse(UserResponse User, string CsrfToken);
public sealed record SetupStatusResponse(bool NeedsSetup);
public sealed record CsrfResponse(string Token);
public sealed record SessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt, string? UserAgent, bool Current);
public sealed record DeviceResponse(Guid Id, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset ExpiresAt);
public sealed record DeviceCodeResponse(string Code, DateTimeOffset ExpiresAt);
public sealed record AccessTokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Password).NotEmpty().MinimumLength(10).MaximumLength(200);
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(80);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().MaximumLength(254);
        RuleFor(request => request.Password).NotEmpty().MaximumLength(200);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword).NotEmpty();
        RuleFor(request => request.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(200);
    }
}

public static class AuthEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth").WithTags("Auth");
        auth.MapGet("/setup-status", SetupStatus).AllowAnonymous();
        auth.MapGet("/csrf", Csrf).AllowAnonymous();
        auth.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting("login");
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting("login");
        auth.MapPost("/logout", Logout).RequireAuthorization();
        auth.MapGet("/me", Me).RequireAuthorization();
        auth.MapPost("/change-password", ChangePassword).RequireAuthorization();
        auth.MapGet("/sessions", Sessions).RequireAuthorization();
        auth.MapDelete("/sessions/{id:guid}", RevokeSession).RequireAuthorization();
        auth.MapGet("/devices", Devices).RequireAuthorization();
        auth.MapDelete("/devices/{id:guid}", RevokeDevice).RequireAuthorization();
        auth.MapPost("/extension/device-code", CreateDeviceCode).RequireAuthorization();
        auth.MapPost("/extension/token", ExchangeDeviceCode).AllowAnonymous().RequireRateLimiting("login");
        auth.MapPost("/extension/refresh", Refresh).AllowAnonymous().RequireRateLimiting("login");
        auth.MapPost("/extension/revoke", RevokeCurrentDevice).RequireAuthorization();
    }

    private static async Task<IResult> SetupStatus(AppDbContext db, CancellationToken cancellationToken)
    {
        var needsSetup = !await db.Users.AnyAsync(cancellationToken);
        return Results.Ok(new SetupStatusResponse(needsSetup));
    }

    private static IResult Csrf(HttpContext httpContext, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        return Results.Ok(new CsrfResponse(tokens.RequestToken ?? ""));
    }

    private static async Task<IResult> Register(
        RegisterRequest? request,
        HttpContext httpContext,
        AppDbContext db,
        PasswordHasher passwords,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (await RejectCsrf(httpContext, antiforgery) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new RegisterRequestValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw AppException.Forbidden("An account already exists. Registration is closed.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = passwords.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
            SecurityStamp = TokenService.NewSecret(),
            CreatedAt = now,
            UpdatedAt = now,
            Settings = new UserSettings { UserId = Guid.Empty }
        };
        user.Settings.UserId = user.Id;
        db.Users.Add(user);
        var session = CreateSession(user, httpContext, now);
        db.WebSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        WriteSessionCookie(httpContext, session);
        return Results.Ok(new AuthResponse(ToUser(user), NewCsrf(httpContext, antiforgery)));
    }

    private static async Task<IResult> Login(
        LoginRequest? request,
        HttpContext httpContext,
        AppDbContext db,
        PasswordHasher passwords,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        if (await RejectCsrf(httpContext, antiforgery) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new LoginRequestValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var email = request.Email.Trim();
        var user = await db.Users.FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        if (user is null || !passwords.Verify(user.PasswordHash, request.Password))
        {
            return Results.Problem(title: "Unauthorized", detail: "Email or password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = CreateSession(user, httpContext, DateTimeOffset.UtcNow);
        db.WebSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        WriteSessionCookie(httpContext, session);
        return Results.Ok(new AuthResponse(ToUser(user), NewCsrf(httpContext, antiforgery)));
    }

    private static async Task<IResult> Logout(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var sessionId = CurrentUser.SessionId(httpContext);
        if (sessionId is not null)
        {
            var session = await db.WebSessions.FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
            if (session is not null)
            {
                session.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        httpContext.Response.Cookies.Delete(SessionAuthHandler.CookieName, CookieOptions(httpContext));
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstAsync(item => item.Id == CurrentUser.Id(httpContext), cancellationToken);
        return Results.Ok(ToUser(user));
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest? request,
        HttpContext httpContext,
        AppDbContext db,
        PasswordHasher passwords,
        CancellationToken cancellationToken)
    {
        if (await RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new ChangePasswordRequestValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        var user = await db.Users.FirstAsync(item => item.Id == userId, cancellationToken);
        if (!passwords.Verify(user.PasswordHash, request.CurrentPassword))
        {
            return Results.Problem(title: "Unauthorized", detail: "Current password is incorrect.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = passwords.Hash(request.NewPassword);
        user.SecurityStamp = TokenService.NewSecret();
        user.UpdatedAt = now;
        var sessions = await db.WebSessions.Where(item => item.UserId == userId && item.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        var devices = await db.DeviceSessions.Where(item => item.UserId == userId && item.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var device in devices)
        {
            device.RevokedAt = now;
        }

        var replacement = CreateSession(user, httpContext, now);
        db.WebSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        WriteSessionCookie(httpContext, replacement);
        return Results.NoContent();
    }

    private static async Task<IResult> Sessions(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(httpContext);
        var current = CurrentUser.SessionId(httpContext);
        var sessions = await db.WebSessions
            .Where(item => item.UserId == userId && item.RevokedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(item => item.LastSeenAt)
            .Select(item => new SessionResponse(item.Id, item.CreatedAt, item.LastSeenAt, item.ExpiresAt, item.UserAgent, item.Id == current))
            .ToListAsync(cancellationToken);
        return Results.Ok(sessions);
    }

    private static async Task<IResult> RevokeSession(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var session = await db.WebSessions.FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUser.Id(httpContext), cancellationToken);
        if (session is null)
        {
            throw AppException.NotFound("Session not found.");
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (CurrentUser.SessionId(httpContext) == id)
        {
            httpContext.Response.Cookies.Delete(SessionAuthHandler.CookieName, CookieOptions(httpContext));
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Devices(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var devices = await db.DeviceSessions
            .Where(item => item.UserId == CurrentUser.Id(httpContext) && item.RevokedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(item => item.LastUsedAt)
            .Select(item => new DeviceResponse(item.Id, item.DeviceName, item.CreatedAt, item.LastUsedAt, item.ExpiresAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(devices);
    }

    private static async Task<IResult> RevokeDevice(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var device = await db.DeviceSessions.FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUser.Id(httpContext), cancellationToken);
        if (device is null)
        {
            throw AppException.NotFound("Device not found.");
        }

        device.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateDeviceCode(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var code = TokenService.NewSecret();
        db.DeviceCodes.Add(new DeviceCode
        {
            Id = Guid.NewGuid(),
            UserId = CurrentUser.Id(httpContext),
            CodeHash = TokenService.HashSecret(code),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2)
        });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new DeviceCodeResponse(code, DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    private static async Task<IResult> ExchangeDeviceCode(
        DeviceCodeExchangeRequest? request,
        AppDbContext db,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.DeviceName))
        {
            throw AppException.BadRequest("Code and device name are required.");
        }

        var hash = TokenService.HashSecret(request.Code.Trim());
        var code = await db.DeviceCodes.Include(item => item.User).FirstOrDefaultAsync(item => item.CodeHash == hash, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (code is null || code.UsedAt is not null || code.ExpiresAt <= now)
        {
            return Results.Problem(title: "Unauthorized", detail: "The device code is invalid or expired.", statusCode: StatusCodes.Status401Unauthorized);
        }

        code.UsedAt = now;
        var refresh = TokenService.NewSecret();
        var device = new DeviceSession
        {
            Id = Guid.NewGuid(),
            UserId = code.UserId,
            RefreshTokenHash = TokenService.HashSecret(refresh),
            DeviceName = request.DeviceName.Trim()[..Math.Min(request.DeviceName.Trim().Length, 120)],
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now.AddDays(90)
        };
        db.DeviceSessions.Add(device);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(Issue(tokens, code.User, device, refresh));
    }

    private static async Task<IResult> Refresh(RefreshRequest? request, AppDbContext db, TokenService tokens, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw AppException.BadRequest("Refresh token is required.");
        }

        var hash = TokenService.HashSecret(request.RefreshToken.Trim());
        var device = await db.DeviceSessions.Include(item => item.User).FirstOrDefaultAsync(item => item.RefreshTokenHash == hash || item.PreviousRefreshTokenHash == hash, cancellationToken);
        if (device is null || device.RevokedAt is not null || device.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Results.Problem(title: "Unauthorized", detail: "The refresh token is invalid.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (device.PreviousRefreshTokenHash == hash)
        {
            device.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Problem(title: "Unauthorized", detail: "Refresh token reuse revoked this device.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var refresh = TokenService.NewSecret();
        device.PreviousRefreshTokenHash = device.RefreshTokenHash;
        device.RefreshTokenHash = TokenService.HashSecret(refresh);
        device.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(Issue(tokens, device.User, device, refresh));
    }

    private static async Task<IResult> RevokeCurrentDevice(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var deviceIdValue = httpContext.User.FindFirst("did")?.Value;
        if (!Guid.TryParse(deviceIdValue, out var deviceId))
        {
            throw AppException.BadRequest("This endpoint revokes the extension device that presented the access token.");
        }

        var device = await db.DeviceSessions.FirstOrDefaultAsync(item => item.Id == deviceId && item.UserId == CurrentUser.Id(httpContext), cancellationToken);
        if (device is not null)
        {
            device.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static UserResponse ToUser(User user) => new(user.Id, user.Email, user.DisplayName);

    private static AccessTokenResponse Issue(TokenService tokens, User user, DeviceSession device, string refresh)
    {
        var minutes = 15;
        return new AccessTokenResponse(tokens.CreateAccessToken(user.Id, device.Id, user.SecurityStamp), refresh, minutes * 60);
    }

    public static async Task<IResult?> RejectCookieCsrf(HttpContext httpContext)
    {
        if (!CurrentUser.IsCookie(httpContext))
        {
            return null;
        }

        var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();
        return await RejectCsrf(httpContext, antiforgery);
    }

    private static async Task<IResult?> RejectCsrf(HttpContext httpContext, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return null;
        }
        catch (AntiforgeryValidationException exception)
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ReadingHub.Csrf");
            logger.LogWarning(exception, "CSRF validation failed.");
            return Results.Problem(title: "Invalid CSRF token", detail: "Send the X-CSRF-TOKEN header from GET /api/v1/auth/csrf.", statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static string NewCsrf(HttpContext httpContext, IAntiforgery antiforgery) =>
        antiforgery.GetAndStoreTokens(httpContext).RequestToken ?? "";

    private static WebSession CreateSession(User user, HttpContext httpContext, DateTimeOffset now)
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var idleDays = configuration.GetValue("Auth:SessionIdleDays", 14);
        return new WebSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddDays(idleDays),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
                ? agent[..Math.Min(agent.Length, 512)]
                : null
        };
    }

    private static void WriteSessionCookie(HttpContext httpContext, WebSession session)
    {
        httpContext.Response.Cookies.Append(SessionAuthHandler.CookieName, session.Id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = CookieSecure(httpContext),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = session.ExpiresAt,
            IsEssential = true
        });
    }

    private static CookieOptions CookieOptions(HttpContext httpContext) => new()
    {
        HttpOnly = true,
        Secure = CookieSecure(httpContext),
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };

    private static bool CookieSecure(HttpContext httpContext)
    {
        var environment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        return !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
    }

    private static async Task<IResult?> Validate<T>(T? model, AbstractValidator<T> validator)
    {
        if (model is null)
        {
            return Results.Problem(title: "Bad request", detail: "Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await validator.ValidateAsync(model);
        if (result.IsValid)
        {
            return null;
        }

        return Results.ValidationProblem(result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
    }

    public static void ConfigureJwt(JwtBearerOptions options, IConfiguration configuration)
    {
        var key = configuration["Auth:JwtSigningKey"] ?? throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 characters.");
        if (key.Length < 32)
        {
            throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 characters.");
        }

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Auth:JwtIssuer"] ?? "readinghub",
            ValidateAudience = true,
            ValidAudience = configuration["Auth:JwtAudience"] ?? "readinghub-extension",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "uid"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var uid = context.Principal?.FindFirst("uid")?.Value;
                var stamp = context.Principal?.FindFirst("stamp")?.Value;
                if (!Guid.TryParse(uid, out var userId))
                {
                    context.Fail("Invalid token.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var current = await db.Users.Where(user => user.Id == userId).Select(user => user.SecurityStamp).FirstOrDefaultAsync();
                if (current is null || current != stamp)
                {
                    context.Fail("Token revoked.");
                }
            }
        };
    }
}
