using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.CardLending;

/// <summary>
/// How many borrowers may hold the card at once, and how long a borrower may take to answer the challenge.
/// </summary>
public sealed record CardLendingLimits(int Leases, TimeSpan RoundTrip)
{
    public static readonly TimeSpan DefaultRoundTrip = TimeSpan.FromMilliseconds(
        CardLendingSettings.DefaultRoundTripMilliseconds
    );
}

/// <summary>
/// The status and the body of one answer on the lending port. A refusal's body is one short word saying why.
/// </summary>
public sealed record LendingReply(int Status, byte[] Body)
{
    public const int Ok = 200;

    public const int Malformed = 400;

    public const int Forbidden = 403;

    public const int NoLease = 409;

    public const int Busy = 429;

    public const int NoCard = 503;

    internal static LendingReply Refused(int status, string reason) => new(status, Encoding.ASCII.GetBytes(reason));
}

/// <summary>
/// Lends this machine's card to borrowers that prove they hold the key: one lease per connection, each with a card
/// connection of its own, given back when the connection closes. ECMs and keys are never written to the log.
/// </summary>
public sealed class CardLender : IDisposable
{
    public const int MostEcmsPerSecond = 32;

    public static readonly TimeSpan ChallengeLife = TimeSpan.FromSeconds(10);

    private const int MostNonces = 2 * LendingProtocol.RoundTripProbes;

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly LendingKey key;

    private readonly Func<IConditionalAccess> openCard;

    private readonly CardLendingLimits limits;

    private readonly TimeProvider time;

    private readonly ILogger? logger;

    private readonly Lock gate = new();

    private readonly Dictionary<string, IssuedChallenge> challenges = [];

    private readonly Dictionary<string, Loan> leases = [];

    private int opening;

    private bool disposed;

    public CardLender(
        LendingKey key,
        Func<IConditionalAccess> openCard,
        CardLendingLimits limits,
        TimeProvider time,
        ILogger? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(openCard);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(time);

        this.key = key;
        this.openCard = openCard;
        this.limits = limits;
        this.time = time;
        this.logger = logger;
    }

    public int LeasesOut
    {
        get
        {
            lock (gate)
            {
                return leases.Count;
            }
        }
    }

    /// <summary>
    /// Starts a lease on the connection again and hands it the first nonce.
    /// </summary>
    public byte[] Challenge(string connection)
    {
        byte[] nonce = LendingProtocol.Nonce();

        lock (gate)
        {
            challenges[connection] = IssuedChallenge.First(nonce, time.GetUtcNow());
        }

        return nonce;
    }

    /// <summary>
    /// Hands the connection a new nonce. An answer to the nonce handed out last, keyed to the transcript and to which
    /// round trip it is, is one round trip and its time is kept for the lease; any other answer starts again.
    /// </summary>
    public byte[] Challenge(string connection, string host, ReadOnlySpan<byte> answer) =>
        Challenge(connection, host, answer, out _);

    /// <summary>
    /// As <see cref="Challenge(string, string, ReadOnlySpan{byte})"/>, saying whether the answer was keyed.
    /// </summary>
    public byte[] Challenge(string connection, string host, ReadOnlySpan<byte> answer, out bool keyed)
    {
        byte[] nonce = LendingProtocol.Nonce();
        DateTimeOffset now = time.GetUtcNow();

        lock (gate)
        {
            challenges.TryGetValue(connection, out IssuedChallenge? previous);
            keyed = previous is not null && IsKeyedAnswer(previous, host, answer, now);
            challenges[connection] = keyed
                ? previous!.Then(nonce, now, answer[..LendingProtocol.NonceLength].ToArray())
                : IssuedChallenge.First(nonce, now);
        }

        return nonce;
    }

    private bool IsKeyedAnswer(IssuedChallenge previous, string host, ReadOnlySpan<byte> answer, DateTimeOffset now)
    {
        if (
            answer.Length is not LendingProtocol.AnswerLength
            || now - previous.At > ChallengeLife
            || previous.Nonces.Length >= MostNonces
        )
        {
            return false;
        }

        ReadOnlySpan<byte> borrowerNonce = answer[..LendingProtocol.NonceLength];
        if (previous.BorrowerNonce is byte[] named && !borrowerNonce.SequenceEqual(named))
        {
            return false;
        }

        byte[] expected = key.Derive(
            LendingProtocol.RoundTripPurpose(previous.Nonces.Length),
            host,
            previous.Transcript,
            borrowerNonce
        );

        return CryptographicOperations.FixedTimeEquals(expected, answer[LendingProtocol.NonceLength..]);
    }

    public LendingReply Lease(string connection, string host, ReadOnlySpan<byte> request, string? peer = null)
    {
        if (request.Length is not LendingProtocol.LeaseRequestLength)
        {
            return LendingReply.Refused(LendingReply.Malformed, "malformed");
        }

        IssuedChallenge? issued;

        lock (gate)
        {
            challenges.Remove(connection, out issued);
        }

        TimeSpan took = issued is null ? TimeSpan.MaxValue : time.GetUtcNow() - issued.At;
        if (issued is null || took > ChallengeLife)
        {
            return LendingReply.Refused(LendingReply.Forbidden, "challenge");
        }

        ReadOnlySpan<byte> borrowerNonce = request[..LendingProtocol.NonceLength];
        byte[] transcript = issued.Transcript;
        byte[] expected = key.Derive(LendingProtocol.BorrowerPurpose, host, transcript, borrowerNonce);
        bool sameBorrower = issued.BorrowerNonce is not byte[] named || borrowerNonce.SequenceEqual(named);
        if (!sameBorrower || !CryptographicOperations.FixedTimeEquals(expected, request[LendingProtocol.NonceLength..]))
        {
            logger?.LogWarning(
                "A borrower at {Peer} could not prove it holds the card-lending key, so the card was not lent.",
                peer
            );

            return LendingReply.Refused(LendingReply.Forbidden, "proof");
        }

        TimeSpan[] samples = [.. issued.Samples, took];
        if (samples.Length < LendingProtocol.RoundTripProbes)
        {
            logger?.LogWarning(
                "A borrower at {Peer} asked for the card after {Probes} round trips rather than {Asked}, so the card was not lent.",
                peer,
                samples.Length,
                LendingProtocol.RoundTripProbes
            );

            return LendingReply.Refused(LendingReply.Forbidden, "probes");
        }

        TimeSpan quickest = samples.Min();
        if (quickest > limits.RoundTrip)
        {
            logger?.LogWarning(
                "A borrower at {Peer} took {Quickest:F1} ms at the quickest of {Probes} round trips, longer than the {Most:F0} ms a household round trip is allowed, so the card was not lent.",
                peer,
                quickest.TotalMilliseconds,
                samples.Length,
                limits.RoundTrip.TotalMilliseconds
            );

            return LendingReply.Refused(LendingReply.Forbidden, "round-trip");
        }

        logger?.LogDebug(
            "A borrower at {Peer} took {Quickest:F2} ms at the quickest of its round trips and {Slowest:F2} ms at the slowest.",
            peer,
            quickest.TotalMilliseconds,
            samples.Max().TotalMilliseconds
        );

        return Lend(connection, host, transcript, borrowerNonce, peer);
    }

    private LendingReply Lend(
        string connection,
        string host,
        byte[] lenderNonce,
        ReadOnlySpan<byte> borrowerNonce,
        string? peer
    )
    {
        lock (gate)
        {
            if (leases.Remove(connection, out Loan? previous))
            {
                previous.Card.Dispose();
            }

            if (disposed || leases.Count + opening >= limits.Leases)
            {
                return LendingReply.Refused(LendingReply.Busy, "busy");
            }

            opening++;
        }

        IConditionalAccess? opened = null;

        try
        {
            opened = openCard();
        }
        catch (DescramblingException error)
        {
            logger?.LogWarning("The card could not be lent to {Peer}: {Why}", peer, error.Message);

            return LendingReply.Refused(LendingReply.NoCard, "card");
        }
        finally
        {
            if (opened is null)
            {
                lock (gate)
                {
                    opening--;
                }
            }
        }

        IConditionalAccess card = opened;

        LendingSession session = LendingSession.ForLender(key, host, lenderNonce, borrowerNonce, card.CaSystemId);
        Loan lease = new(card, session, peer, time.GetUtcNow());
        bool kept;

        lock (gate)
        {
            opening--;
            kept = !disposed;
            if (kept)
            {
                leases[connection] = lease;
            }
        }

        if (!kept)
        {
            card.Dispose();

            return LendingReply.Refused(LendingReply.NoCard, "stopping");
        }

        logger?.LogInformation("The card was lent to a borrower at {Peer}.", peer);

        byte[] reply = new byte[LendingProtocol.LeaseReplyLength];
        BinaryPrimitives.WriteUInt16BigEndian(reply, (ushort)card.CaSystemId);
        key.Derive(LendingProtocol.LenderPurpose, host, lenderNonce, borrowerNonce).CopyTo(reply, sizeof(ushort));

        return new LendingReply(LendingReply.Ok, reply);
    }

    public LendingReply Answer(string connection, ReadOnlySpan<byte> ecmBody)
    {
        Loan? lease;

        lock (gate)
        {
            leases.TryGetValue(connection, out lease);
        }

        if (lease is null)
        {
            return LendingReply.Refused(LendingReply.NoLease, "lease");
        }

        if (ecmBody.Length is 0 or > ConditionalAccessCard.LongestEcmBody)
        {
            return LendingReply.Refused(LendingReply.Malformed, "malformed");
        }

        if (!lease.Admit(time.GetUtcNow()))
        {
            return LendingReply.Refused(LendingReply.Busy, "rate");
        }

        EcmAnswer answer;

        try
        {
            answer = lease.Card.Answer(ecmBody);
        }
        catch (DescramblingException error)
        {
            End(connection, lease);
            logger?.LogWarning(
                "The card stopped answering while lent to {Peer}, so the lease ended: {Why}",
                lease.Peer,
                error.Message
            );

            return LendingReply.Refused(LendingReply.NoCard, "card");
        }

        lease.Count(answer.Granted);

        return new LendingReply(LendingReply.Ok, lease.Session.Seal(answer));
    }

    public void Release(string connection)
    {
        Loan? lease;

        lock (gate)
        {
            challenges.Remove(connection);
            leases.TryGetValue(connection, out lease);
        }

        if (lease is not null)
        {
            End(connection, lease);
        }
    }

    private void End(string connection, Loan lease)
    {
        lock (gate)
        {
            if (!leases.TryGetValue(connection, out Loan? held) || !ReferenceEquals(held, lease))
            {
                return;
            }

            leases.Remove(connection);
        }

        lease.Card.Dispose();
        logger?.LogInformation(
            "The card was given back by the borrower at {Peer} after {Seconds:F0} s: {Granted} ECMs unlocked, {Refused} refused.",
            lease.Peer,
            (time.GetUtcNow() - lease.Since).TotalSeconds,
            lease.Granted,
            lease.Refused
        );
    }

    public void Dispose()
    {
        List<Loan> held;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            held = [.. leases.Values];
            leases.Clear();
            challenges.Clear();
        }

        foreach (Loan lease in held)
        {
            lease.Card.Dispose();
        }
    }

    /// <summary>
    /// The nonces handed out on one connection so far, when the last one was, who answered them, and how long each
    /// keyed answer took.
    /// </summary>
    private sealed record IssuedChallenge(byte[][] Nonces, DateTimeOffset At, byte[]? BorrowerNonce, TimeSpan[] Samples)
    {
        public byte[] Transcript => [.. Nonces.SelectMany(nonce => nonce)];

        public static IssuedChallenge First(byte[] nonce, DateTimeOffset at) => new([nonce], at, null, []);

        public IssuedChallenge Then(byte[] nonce, DateTimeOffset at, byte[] borrowerNonce) =>
            new([.. Nonces, nonce], at, borrowerNonce, [.. Samples, at - At]);
    }

    private sealed class Loan(IConditionalAccess card, LendingSession session, string? peer, DateTimeOffset since)
    {
        private readonly Lock gate = new();

        private DateTimeOffset windowStart = since;

        private int inWindow;

        private int granted;

        private int refused;

        public IConditionalAccess Card { get; } = card;

        public LendingSession Session { get; } = session;

        public string? Peer { get; } = peer;

        public DateTimeOffset Since { get; } = since;

        public int Granted => Volatile.Read(ref granted);

        public int Refused => Volatile.Read(ref refused);

        public bool Admit(DateTimeOffset now)
        {
            lock (gate)
            {
                if (now - windowStart >= Second)
                {
                    windowStart = now;
                    inWindow = 0;
                }

                if (inWindow >= MostEcmsPerSecond)
                {
                    return false;
                }

                inWindow++;

                return true;
            }
        }

        public void Count(bool wasGranted)
        {
            if (wasGranted)
            {
                Interlocked.Increment(ref granted);
            }
            else
            {
                Interlocked.Increment(ref refused);
            }
        }
    }
}
