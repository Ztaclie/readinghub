using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ReadingHub.Api.Infrastructure;

public sealed class TokenService(IConfiguration configuration)
{
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes);
    }

    public string CreateAccessToken(Guid userId, Guid deviceId, string securityStamp)
    {
        var key = configuration["Auth:JwtSigningKey"] ?? throw new InvalidOperationException("Auth:JwtSigningKey is required.");
        var issuer = configuration["Auth:JwtIssuer"] ?? "readinghub";
        var audience = configuration["Auth:JwtAudience"] ?? "readinghub-extension";
        var minutes = configuration.GetValue("Auth:AccessTokenMinutes", 15);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim("uid", userId.ToString()),
                new Claim("did", deviceId.ToString()),
                new Claim("stamp", securityStamp),
                new Claim("amr", "bearer"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
