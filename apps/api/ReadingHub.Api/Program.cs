using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ReadingHub.Api.Data;
using ReadingHub.Api.Features.Auth;
using ReadingHub.Api.Features.Bookmarks;
using ReadingHub.Api.Features.Reading;
using ReadingHub.Api.Infrastructure;
using ReadingHub.Api.Security;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, _, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

var jwtKey = builder.Configuration["Auth:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("Set Auth:JwtSigningKey to a random string of at least 32 characters.");
}

if (builder.Environment.IsProduction() && jwtKey.Contains("dev-only", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Replace the development JWT signing key before running in Production.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<MetadataParser>();
builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
builder.Services.AddScoped<SafeHttpFetcher>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("ReadingHub");

var secureCookies = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "rh_csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthentication(options => options.DefaultScheme = "smart")
    .AddPolicyScheme("smart", "Cookie or bearer", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : SessionAuthHandler.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, null)
    .AddJwtBearer(options => AuthEndpoints.ConfigureJwt(options, builder.Configuration));
builder.Services.AddAuthorization();

var webOrigin = builder.Configuration["Cors:WebOrigin"] ?? "http://localhost:5173";
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Auth:LoginPermitLimit", 10),
            Window = TimeSpan.FromMinutes(1)
        }));
    options.AddPolicy("api", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.FindFirst("uid")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
});

var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "database", tags: ["ready"]);
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (error is AppException appError)
    {
        await Results.Problem(
            title: appError.Title,
            detail: appError.Message,
            statusCode: appError.StatusCode,
            extensions: appError.Extensions).ExecuteAsync(context);
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(error, "Unhandled exception");
    await Results.Problem(title: "Server error", detail: "Something went wrong.", statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
}));
app.UseSerilogRequestLogging();
if (app.Environment.IsProduction())
{
    app.UseForwardedHeaders();
}

app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api/v1").RequireRateLimiting("api");
AuthEndpoints.Map(api);
BookmarkEndpoints.Map(api);
ReadingEndpoints.Map(api);

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    await using (var acquire = connection.CreateCommand())
    {
        acquire.CommandText = "SELECT pg_advisory_lock(847291)";
        await acquire.ExecuteNonQueryAsync();
    }

    try
    {
        await db.Database.MigrateAsync();
    }
    finally
    {
        await using var release = connection.CreateCommand();
        release.CommandText = "SELECT pg_advisory_unlock(847291)";
        await release.ExecuteNonQueryAsync();
    }
}

app.Run();

public partial class Program;
