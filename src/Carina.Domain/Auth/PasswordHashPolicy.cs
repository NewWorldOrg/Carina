namespace Carina.Domain.Auth;

public sealed record PasswordHashPolicy
{
    public const int LeastMemoryKibibytes = 7168;

    public const int LeastWorkKibibyteRounds = 35840;

    public const int LeastSaltLength = 16;

    public const int LeastDigestLength = 32;

    public PasswordHashPolicy(
        int memoryKibibytes,
        int iterations,
        int parallelism,
        int saltLength,
        int digestLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memoryKibibytes, LeastMemoryKibibytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(saltLength, LeastSaltLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(digestLength, LeastDigestLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            (long)memoryKibibytes * iterations,
            LeastWorkKibibyteRounds,
            nameof(memoryKibibytes));

        MemoryKibibytes = memoryKibibytes;
        Iterations = iterations;
        Parallelism = parallelism;
        SaltLength = saltLength;
        DigestLength = digestLength;
    }

    public static PasswordHashPolicy Default { get; } = new(19456, 2, 1, LeastSaltLength, LeastDigestLength);

    public int MemoryKibibytes { get; }

    public int Iterations { get; }

    public int Parallelism { get; }

    public int SaltLength { get; }

    public int DigestLength { get; }

    public bool NeedsRehash(PasswordHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        return hash.MemoryKibibytes < MemoryKibibytes
            || hash.Iterations < Iterations
            || hash.DigestLength < DigestLength;
    }
}
