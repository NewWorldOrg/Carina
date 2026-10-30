using System.Net;
using System.Net.Sockets;
using System.Text;

using Carina.Driver.Descrambling;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.CardLending;

/// <summary>
/// Which lender to borrow a card from, by the address it is reached at and the host name it answers to.
/// </summary>
public sealed record CardLoan(IPEndPoint Lender, string HostName, int HopLimit, LendingKey Key);

/// <summary>
/// A card lent by another driver in the household: each ECM goes to the lender over one connection of its own, and the
/// keys come back sealed for this lease. Anything that stops the lender answering is the card not answering.
/// </summary>
public sealed class BorrowedCard : IConditionalAccess
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private const int LongestReason = 32;

    private readonly HttpClient client;

    private readonly CardLoan loan;

    private readonly ILogger? logger;

    private readonly Lock gate = new();

    private LendingSession session;

    private bool disposed;

    private BorrowedCard(HttpClient client, CardLoan loan, LendingSession session, ILogger? logger)
    {
        this.client = client;
        this.loan = loan;
        this.session = session;
        this.logger = logger;
    }

    public int CaSystemId => session.CaSystemId;

    public int Granted { get; private set; }

    public int Refused { get; private set; }

    public static BorrowedCard Open(CardLoan loan, ILogger? logger = null) => Open(loan, Connecting(loan), logger);

    public static BorrowedCard Open(CardLoan loan, HttpMessageHandler handler, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loan);
        ArgumentNullException.ThrowIfNull(handler);

        HttpClient client = new(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"http://{loan.Lender}"),
            Timeout = Patience,
        };

        try
        {
            return new BorrowedCard(client, loan, Lease(client, loan), logger);
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    public EcmAnswer Answer(ReadOnlySpan<byte> ecmBody)
    {
        byte[] body = ecmBody.ToArray();

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            (int status, byte[] reply) = Send(client, loan, LendingProtocol.EcmPath, body);
            if (status is LendingReply.NoLease)
            {
                session = Lease(client, loan);
                (status, reply) = Send(client, loan, LendingProtocol.EcmPath, body);
            }

            if (status is not LendingReply.Ok)
            {
                throw Refusal("an ECM", status, reply);
            }

            EcmAnswer answer = session.Open(reply);
            if (answer.Granted)
            {
                Granted++;
            }
            else
            {
                Refused++;
            }

            return answer;
        }
    }

    private static SocketsHttpHandler Connecting(CardLoan loan) =>
        new()
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 1,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = Patience,
            ConnectCallback = async (_, cancellationToken) =>
            {
                Socket socket = new(loan.Lender.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                try
                {
                    HopLimit.Apply(socket, loan.HopLimit);
                    await socket.ConnectAsync(loan.Lender, cancellationToken);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();

                    throw;
                }
            },
        };

    private static LendingSession Lease(HttpClient client, CardLoan loan)
    {
        byte[] challenge = Expect(client, loan, LendingProtocol.ChallengePath, [], "the challenge");
        BorrowerHandshake handshake = BorrowerHandshake.Begin(loan.Key, loan.HostName, challenge);

        return handshake.Accept(Expect(client, loan, LendingProtocol.LeasePath, handshake.Request, "the lease"));
    }

    private static byte[] Expect(HttpClient client, CardLoan loan, string path, byte[] body, string what)
    {
        (int status, byte[] reply) = Send(client, loan, path, body);

        return status is LendingReply.Ok ? reply : throw Refusal(what, status, reply);
    }

    private static (int Status, byte[] Body) Send(HttpClient client, CardLoan loan, string path, byte[] body)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Headers.Host = loan.HostName;

        try
        {
            using HttpResponseMessage response = client.Send(request);
            using MemoryStream reply = new();
            response.Content.ReadAsStream().CopyTo(reply);

            return ((int)response.StatusCode, reply.ToArray());
        }
        catch (HttpRequestException error)
        {
            throw new DescramblingException($"The lender at {loan.Lender} could not be reached: {error.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new DescramblingException(
                $"The lender at {loan.Lender} did not answer within {Patience.TotalSeconds:F0} seconds."
            );
        }
        catch (IOException error)
        {
            throw new DescramblingException($"The connection to the lender at {loan.Lender} broke: {error.Message}");
        }
    }

    private static DescramblingException Refusal(string what, int status, byte[] reply)
    {
        string reason = new(
            [.. Encoding.ASCII.GetString(reply).Where(letter => letter is >= 'a' and <= 'z' or '-').Take(LongestReason)]
        );

        return new DescramblingException(
            $"The lender refused {what} ({status}{(reason.Length is 0 ? string.Empty : $", {reason}")})."
        );
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        client.Dispose();
        logger?.LogInformation(
            "The borrowed card was given back to {Lender}: {Granted} ECMs unlocked, {Refused} refused.",
            loan.Lender,
            Granted,
            Refused
        );
    }
}

/// <summary>
/// Opens a descrambler per tuner over a card borrowed for it alone.
/// </summary>
public sealed class BorrowedCards : IDescramblerFactory
{
    private readonly Func<IConditionalAccess> borrow;

    private readonly ILogger? logger;

    private BorrowedCards(Func<IConditionalAccess> borrow, ILogger? logger)
    {
        this.borrow = borrow;
        this.logger = logger;
    }

    public static BorrowedCards Over(Func<IConditionalAccess> borrow, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(borrow);

        return new BorrowedCards(borrow, logger);
    }

    public static BorrowedCards From(CardLoan loan, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(loan);

        return new BorrowedCards(() => BorrowedCard.Open(loan, logger), logger);
    }

    public bool Unscrambles => true;

    public CardIdentity? CardInHand => null;

    public IDescrambler? Open(HeadHold hold)
    {
        IConditionalAccess card;

        try
        {
            card = borrow();
        }
        catch (DescramblingException error)
        {
            logger?.LogWarning("The lender's card could not be borrowed, so it is asked for again: {Why}", error.Message);

            return null;
        }

        try
        {
            return new TransportStreamDescrambler(card, logger, hold: hold);
        }
        catch
        {
            card.Dispose();

            throw;
        }
    }
}
