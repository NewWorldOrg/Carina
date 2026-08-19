using System.Globalization;
using System.Security.Cryptography;

using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class PasswordHash : CommonValueObject<string>
{
    public const string Algorithm = "argon2id";

    public const int Version = 19;

    private readonly byte[] salt;

    private readonly byte[] digest;

    public PasswordHash(string value)
        : base(value)
    {
        string[] fields = Value.Split('$');

        if (fields.Length != 6 || fields[0].Length != 0)
        {
            throw new ArgumentException(
                "A stored password hash is an encoded PHC string.",
                nameof(value));
        }

        if (fields[1] != Algorithm)
        {
            throw new ArgumentException(
                $"Passwords are hashed with {Algorithm}, and a hash from anything else cannot be checked.",
                nameof(value));
        }

        if (fields[2] != $"v={Version}")
        {
            throw new ArgumentException(
                $"Passwords are hashed with {Algorithm} version {Version}.",
                nameof(value));
        }

        (MemoryKibibytes, Iterations, Parallelism) = Costs(fields[3], nameof(value));
        salt = Decoded(fields[4], nameof(value));
        digest = Decoded(fields[5], nameof(value));

        if (salt.Length == 0 || digest.Length == 0)
        {
            throw new ArgumentException(
                "A stored password hash carries both the salt it used and the digest it produced.",
                nameof(value));
        }
    }

    public int MemoryKibibytes { get; }

    public int Iterations { get; }

    public int Parallelism { get; }

    public int DigestLength => digest.Length;

    public static PasswordHash Encode(
        PasswordHashPolicy policy,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> digest)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (salt.Length != policy.SaltLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(salt),
                salt.Length,
                $"The policy asks for a {policy.SaltLength} byte salt.");
        }

        if (digest.Length != policy.DigestLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(digest),
                digest.Length,
                $"The policy asks for a {policy.DigestLength} byte digest.");
        }

        return new PasswordHash(
            $"${Algorithm}$v={Version}$m={policy.MemoryKibibytes},t={policy.Iterations},p={policy.Parallelism}$"
            + $"{Encoded(salt)}${Encoded(digest)}");
    }

    public byte[] CopySalt() => [.. salt];

    public bool Matches(ReadOnlySpan<byte> candidate) => CryptographicOperations.FixedTimeEquals(digest, candidate);

    public override string ToString() => "(password hash)";

    private static (int Memory, int Iterations, int Parallelism) Costs(string field, string parameterName)
    {
        string[] costs = field.Split(',');

        if (costs.Length != 3)
        {
            throw new ArgumentException(
                "A stored password hash names the memory, rounds and lanes it was made with.",
                parameterName);
        }

        return (
            Cost(costs[0], "m=", parameterName),
            Cost(costs[1], "t=", parameterName),
            Cost(costs[2], "p=", parameterName));
    }

    private static int Cost(string field, string prefix, string parameterName)
    {
        if (!field.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(field[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int cost)
            || cost < 1)
        {
            throw new ArgumentException(
                $"A stored password hash carries {prefix.TrimEnd('=')} as a positive number.",
                parameterName);
        }

        return cost;
    }

    private static string Encoded(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] Decoded(string field, string parameterName)
    {
        string padded = field.PadRight(field.Length + ((4 - (field.Length % 4)) % 4), '=');
        byte[] buffer = new byte[padded.Length / 4 * 3];

        if (!Convert.TryFromBase64String(padded, buffer, out int written))
        {
            throw new ArgumentException(
                "A stored password hash carries its salt and digest as base64.",
                parameterName);
        }

        return buffer[..written];
    }
}
