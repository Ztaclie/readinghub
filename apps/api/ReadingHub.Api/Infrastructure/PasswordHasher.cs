using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace ReadingHub.Api.Infrastructure;

public sealed class PasswordHasher
{
    public string Hash(string password)
    {
        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 2,
            MemoryCost = 19456,
            Lanes = 1,
            Threads = 1,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = RandomNumberGenerator.GetBytes(16),
            HashLength = 32
        };
        return Argon2.Hash(config);
    }

    public bool Verify(string encoded, string password)
    {
        try
        {
            return Argon2.Verify(encoded, Encoding.UTF8.GetBytes(password));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
