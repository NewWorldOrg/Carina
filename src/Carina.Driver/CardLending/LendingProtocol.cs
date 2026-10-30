using System.Buffers.Binary;
using System.Security.Cryptography;

using Carina.Driver.Descrambling;

namespace Carina.Driver.CardLending;

/// <summary>
/// What crosses the wire between a lending driver and a borrowing one. A lease starts with the lender's nonce; the
/// borrower answers with its own nonce and a proof derived from the shared key, the host it asked for and both nonces;
/// the lender answers with the card's CA system and a proof of its own. Every answer to an ECM is then sealed with
/// AES-GCM under a key derived the same way, so the keys never cross the wire in the clear.
/// </summary>
public static class LendingProtocol
{
    public const string ChallengePath = "/card-lending/v1/challenge";

    public const string LeasePath = "/card-lending/v1/lease";

    public const string EcmPath = "/card-lending/v1/ecm";

    public const int NonceLength = 32;

    public const int ProofLength = 32;

    public const int LeaseRequestLength = NonceLength + ProofLength;

    public const int LeaseReplyLength = sizeof(ushort) + ProofLength;

    public const int LongestBody = 512;

    internal const string BorrowerPurpose = "carina card lending v1: borrower";

    internal const string LenderPurpose = "carina card lending v1: lender";

    internal const string SessionPurpose = "carina card lending v1: session";

    internal const int SealHeaderLength = sizeof(ushort) + sizeof(ulong);

    internal const int TagLength = 16;

    internal static byte[] Nonce() => RandomNumberGenerator.GetBytes(NonceLength);
}

/// <summary>
/// The borrower's half of starting a lease.
/// </summary>
public sealed class BorrowerHandshake
{
    private readonly LendingKey key;

    private readonly string host;

    private readonly byte[] lenderNonce;

    private readonly byte[] borrowerNonce;

    private BorrowerHandshake(LendingKey key, string host, byte[] lenderNonce, byte[] borrowerNonce)
    {
        this.key = key;
        this.host = host;
        this.lenderNonce = lenderNonce;
        this.borrowerNonce = borrowerNonce;
    }

    public static BorrowerHandshake Begin(LendingKey key, string host, ReadOnlySpan<byte> lenderNonce)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(host);

        if (lenderNonce.Length is not LendingProtocol.NonceLength)
        {
            throw new DescramblingException(
                $"The lender's challenge was {lenderNonce.Length} bytes rather than {LendingProtocol.NonceLength}."
            );
        }

        return new BorrowerHandshake(key, host, lenderNonce.ToArray(), LendingProtocol.Nonce());
    }

    /// <summary>
    /// The borrower's nonce followed by its proof.
    /// </summary>
    public byte[] Request =>
        [.. borrowerNonce, .. key.Derive(LendingProtocol.BorrowerPurpose, host, lenderNonce, borrowerNonce)];

    /// <summary>
    /// Takes the lender's answer to <see cref="Request"/> and starts the lease, once the lender has shown it holds the key.
    /// </summary>
    public LendingSession Accept(ReadOnlySpan<byte> reply)
    {
        if (reply.Length is not LendingProtocol.LeaseReplyLength)
        {
            throw new DescramblingException(
                $"The lender answered the lease with {reply.Length} bytes rather than {LendingProtocol.LeaseReplyLength}."
            );
        }

        byte[] expected = key.Derive(LendingProtocol.LenderPurpose, host, lenderNonce, borrowerNonce);
        if (!CryptographicOperations.FixedTimeEquals(expected, reply[sizeof(ushort)..]))
        {
            throw new DescramblingException(
                "The lender could not prove it holds the card-lending key, so nothing it hands back is taken."
            );
        }

        return new LendingSession(
            key.Derive(LendingProtocol.SessionPurpose, host, lenderNonce, borrowerNonce),
            BinaryPrimitives.ReadUInt16BigEndian(reply)
        );
    }
}

/// <summary>
/// One lease as either side holds it: the CA system of the lent card and the key its answers are sealed with.
/// </summary>
public sealed class LendingSession
{
    private readonly byte[] sealingKey;

    private readonly Lock gate = new();

    private ulong lastCounter;

    internal LendingSession(byte[] sealingKey, int caSystemId)
    {
        this.sealingKey = sealingKey;
        CaSystemId = caSystemId;
    }

    public int CaSystemId { get; }

    internal static LendingSession ForLender(
        LendingKey key,
        string host,
        ReadOnlySpan<byte> lenderNonce,
        ReadOnlySpan<byte> borrowerNonce,
        int caSystemId
    ) => new(key.Derive(LendingProtocol.SessionPurpose, host, lenderNonce, borrowerNonce), caSystemId);

    internal byte[] Seal(EcmAnswer answer)
    {
        ulong counter;

        lock (gate)
        {
            counter = ++lastCounter;
        }

        ReadOnlySpan<byte> plain = answer.Granted ? answer.KeyMaterial.Span : [];
        byte[] sealedAnswer = new byte[LendingProtocol.SealHeaderLength + plain.Length + LendingProtocol.TagLength];
        BinaryPrimitives.WriteUInt16BigEndian(sealedAnswer, answer.ReturnCode);
        BinaryPrimitives.WriteUInt64BigEndian(sealedAnswer.AsSpan(sizeof(ushort)), counter);

        using AesGcm aes = new(sealingKey, LendingProtocol.TagLength);
        aes.Encrypt(
            NonceFor(counter),
            plain,
            sealedAnswer.AsSpan(LendingProtocol.SealHeaderLength, plain.Length),
            sealedAnswer.AsSpan(LendingProtocol.SealHeaderLength + plain.Length),
            sealedAnswer.AsSpan(0, LendingProtocol.SealHeaderLength)
        );

        return sealedAnswer;
    }

    /// <summary>
    /// Opens an answer the lender sealed for this lease. One sealed for another lease, changed on the way, or handed
    /// back a second time is refused.
    /// </summary>
    public EcmAnswer Open(ReadOnlySpan<byte> sealedAnswer)
    {
        int plainLength = sealedAnswer.Length - LendingProtocol.SealHeaderLength - LendingProtocol.TagLength;
        if (plainLength is not (0 or EcmAnswer.KeyMaterialLength))
        {
            throw new DescramblingException(
                $"The lender's answer to an ECM was {sealedAnswer.Length} bytes, which is no length an answer has."
            );
        }

        ushort returnCode = BinaryPrimitives.ReadUInt16BigEndian(sealedAnswer);
        ulong counter = BinaryPrimitives.ReadUInt64BigEndian(sealedAnswer[sizeof(ushort)..]);
        byte[] plain = new byte[plainLength];

        try
        {
            using AesGcm aes = new(sealingKey, LendingProtocol.TagLength);
            aes.Decrypt(
                NonceFor(counter),
                sealedAnswer.Slice(LendingProtocol.SealHeaderLength, plainLength),
                sealedAnswer[(LendingProtocol.SealHeaderLength + plainLength)..],
                plain,
                sealedAnswer[..LendingProtocol.SealHeaderLength]
            );
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new DescramblingException(
                "The lender's answer to an ECM was not sealed for this lease, so it is not taken."
            );
        }

        lock (gate)
        {
            if (counter <= lastCounter)
            {
                throw new DescramblingException(
                    "The lender's answer to an ECM was one already taken, so it is not taken again."
                );
            }

            lastCounter = counter;
        }

        return plainLength is 0 ? new EcmAnswer(returnCode, null) : EcmAnswer.Granting(returnCode, plain);
    }

    private static byte[] NonceFor(ulong counter)
    {
        byte[] nonce = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);

        return nonce;
    }

    public override string ToString() => nameof(LendingSession);
}
