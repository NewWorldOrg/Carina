using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Carina.Driver.Descrambling;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.CardLending;

/// <summary>
/// How many borrowers may hold the card at once, and how long a borrower may take to answer the challenge.
/// </summary>
public sealed record CardLendingLimits(int Leases, TimeSpan? RoundTrip);

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

    public byte[] Challenge(string connection)
    {
        byte[] nonce = LendingProtocol.Nonce();

        lock (gate)
        {
            challenges[connection] = new IssuedChallenge(nonce, time.GetUtcNow());
        }

        return nonce;
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
        byte[] expected = key.Derive(LendingProtocol.BorrowerPurpose, host, issued.Nonce, borrowerNonce);
        if (!CryptographicOperations.FixedTimeEquals(expected, request[LendingProtocol.NonceLength..]))
        {
            logger?.LogWarning(
                "A borrower at {Peer} could not prove it holds the card-lending key, so the card was not lent.",
                peer
            );

            return LendingReply.Refused(LendingReply.Forbidden, "proof");
        }

        if (limits.RoundTrip is TimeSpan most && took > most)
        {
            logger?.LogWarning(
                "A borrower at {Peer} took {Took:F1} ms to answer the challenge, longer than the {Most:F0} ms a household round trip is allowed, so the card was not lent.",
                peer,
                took.TotalMilliseconds,
                most.TotalMilliseconds
            );

            return LendingReply.Refused(LendingReply.Forbidden, "round-trip");
        }

        return Lend(connection, host, issued.Nonce, borrowerNonce, peer);
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

        IConditionalAccess card;

        try
        {
            card = openCard();
        }
        catch (DescramblingException error)
        {
            lock (gate)
            {
                opening--;
            }

            logger?.LogWarning("The card could not be lent to {Peer}: {Why}", peer, error.Message);

            return LendingReply.Refused(LendingReply.NoCard, "card");
        }

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

    private sealed record IssuedChallenge(byte[] Nonce, DateTimeOffset At);

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
