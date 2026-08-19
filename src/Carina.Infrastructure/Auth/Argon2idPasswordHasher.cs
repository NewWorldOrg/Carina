using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Auth;

using Konscious.Security.Cryptography;

namespace Carina.Infrastructure.Auth;

public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    public PasswordHash Hash(string password, PasswordHashPolicy policy)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentNullException.ThrowIfNull(policy);

        byte[] salt = RandomNumberGenerator.GetBytes(policy.SaltLength);
        byte[] digest = Derive(
            password,
            policy.MemoryKibibytes,
            policy.Iterations,
            policy.Parallelism,
            salt,
            policy.DigestLength);

        return PasswordHash.Encode(policy, salt, digest);
    }

    public bool Matches(string password, PasswordHash hash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentNullException.ThrowIfNull(hash);

        byte[] digest = Derive(
            password,
            hash.MemoryKibibytes,
            hash.Iterations,
            hash.Parallelism,
            hash.CopySalt(),
            hash.DigestLength);

        return hash.Matches(digest);
    }

    private static byte[] Derive(
        string password,
        int memoryKibibytes,
        int iterations,
        int parallelism,
        byte[] salt,
        int digestLength)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKibibytes,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon.GetBytes(digestLength);
    }
}
