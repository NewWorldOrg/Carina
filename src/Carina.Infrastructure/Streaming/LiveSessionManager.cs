using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveSessionManager(
    LiveSessionSettings settings,
    LiveFanoutSettings fanouts,
    LiveTranscodeSettings transcoding,
    ILiveSupply supply,
    ILiveTranscoderFactory transcoders,
    TimeProvider clock,
    IAppEventPublisher events) : ILiveSessionManager, ILiveSessionLedger, IAsyncDisposable
{
    public const int Attempts = 2;

    private readonly Lock gate = new();

    private readonly Dictionary<LiveSessionKey, LiveSession> sessions = [];

    private readonly Dictionary<(NetworkId Network, ServiceId Service), LiveReception> receptions = [];

    private readonly List<LiveSession> leaving = [];

    public IReadOnlyList<LiveSessionKey> Keys
    {
        get
        {
            lock (gate)
            {
                return [.. sessions.Keys];
            }
        }
    }

    public async Task<IReadOnlyList<LiveSessionView>> RunningAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<SessionId, long> onTheWayIn = await supply.DroppedOnTheWayInAsync(cancellationToken);
        List<LiveSession> running;

        lock (gate)
        {
            running = [.. sessions.Values];
        }

        return
        [
            .. running.Select(session => new LiveSessionView(
                session.Key,
                session.Viewers,
                session.Startup.Current ?? LiveStartup.NotStarted,
                session.Dropped,
                session.Queued,
                session.Watching,
                Lost(session, onTheWayIn))),
        ];
    }

    private static long? Lost(LiveSession session, IReadOnlyDictionary<SessionId, long> onTheWayIn)
        => session.Supply is { } supplied && onTheWayIn.TryGetValue(supplied, out long dropped)
            ? dropped
            : null;

    public int Viewers(LiveSessionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (gate)
        {
            return sessions.TryGetValue(key, out LiveSession? session) ? session.Viewers : 0;
        }
    }

    public ILiveStartup? Startup(LiveSessionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (gate)
        {
            return sessions.TryGetValue(key, out LiveSession? session) ? session.Startup : null;
        }
    }

    public async Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        LiveJoin join = await SeatedAsync(key, cancellationToken);

        bool freed = join.Refusal is LiveRefusal.NoTunerFree
                     && await LetGoOfWhatNobodyIsWatchingAsync(key, cancellationToken);

        return freed ? await SeatedAsync(key, cancellationToken) : join;
    }

    public async Task<LiveHandover> HandOverAsync(LiveChannelKey channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        LiveReception reading;

        lock (gate)
        {
            reading = Receiving(channel.Network, channel.Service);
        }

        LiveSupplyStart opened;

        try
        {
            opened = await reading.OpenAsync(cancellationToken);
        }
        catch (Exception)
        {
            reading.Detach();

            throw;
        }

        if (opened.Stream is null)
        {
            reading.Detach();

            return LiveHandover.Refused(opened.Refusal!.Value);
        }

        return LiveHandover.Carrying(new LiveHandedOverReading(reading, settings, clock));
    }

    public async ValueTask DisposeAsync()
    {
        List<LiveSession> closing;
        List<LiveReception> reading;

        lock (gate)
        {
            closing = [.. sessions.Values];
            reading = [.. receptions.Values];
        }

        foreach (LiveSession session in closing)
        {
            session.Close();
        }

        await Task.WhenAll(closing.Select(session => session.Life));

        foreach (LiveReception reception in reading)
        {
            reception.Close();
        }

        await Task.WhenAll(reading.Select(reception => reception.Life));
        await Task.WhenAll(reading.Select(reception => reception.Holding));
    }

    private async Task<LiveJoin> SeatedAsync(LiveSessionKey key, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (await Expected(key).JoinAsync(cancellationToken) is { } join)
            {
                return join;
            }
        }

        return LiveJoin.Refused(
            LiveRefusal.TranscoderWouldNotStart,
            "what the transcoder wrote ended before a viewer could be seated.");
    }

    /// <summary>
    /// Gives up the readings nobody is watching any more, and waits for the ones already on their
    /// way out.
    /// </summary>
    /// <remarks>
    /// A session leaves the ledger the moment it is closed, but the tuner behind it is let go of at
    /// the end of its teardown, by the reading rather than by the session. Between those two points
    /// the ledger holds nothing to give up and the tuner is not free yet, which is where a viewer
    /// changing channel on a machine with one tuner was told there was none.
    /// </remarks>
    private async Task<bool> LetGoOfWhatNobodyIsWatchingAsync(
        LiveSessionKey asked,
        CancellationToken cancellationToken)
    {
        List<LiveSession> given;
        List<LiveSession> going;

        lock (gate)
        {
            given =
            [
                .. sessions.Values.Where(session => !session.Key.Equals(asked) && session.NobodyIsWatching),
            ];
            going = [.. StillLettingGo().Where(session => !session.Key.Equals(asked))];
        }

        foreach (LiveSession session in given)
        {
            session.Close();
        }

        List<LiveSession> letting = [.. given, .. going];

        return letting.Count > 0 && await LetGoOfTheTunerAsync(letting, cancellationToken);
    }

    /// <summary>
    /// Waits for what is being let go of to reach the driver, and for no longer than one wait: a
    /// teardown that will not end is a tuner that never comes free, and the refusal stands.
    /// </summary>
    private async Task<bool> LetGoOfTheTunerAsync(
        IReadOnlyList<LiveSession> letting,
        CancellationToken cancellationToken)
    {
        try
        {
            await EndedAsync(letting).WaitAsync(settings.LongestWaitForATunerToComeFree, clock, cancellationToken);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task EndedAsync(IReadOnlyList<LiveSession> letting)
    {
        await Task.WhenAll(letting.Select(session => Quietly(session.Life)));

        // The tuner is let go of by the reading, not by the session, so the asking viewer waits
        // for the reading to finish rather than being refused by a tuner on its way out.
        await Task.WhenAll(
            letting.Select(session => session.Reception).Distinct().Select(reading => Quietly(reading.Life)));
    }

    /// <summary>
    /// How a teardown ended is that session's own business: what is waited for here is that it is
    /// over, and the viewer asking for a tuner is not the one to be handed its failure.
    /// </summary>
    private static async Task Quietly(Task ending)
    {
        try
        {
            await ending;
        }
        catch (Exception)
        {
            return;
        }
    }

    /// <summary>
    /// The sessions that have left the ledger and have not yet let go of what they were reading.
    /// </summary>
    private IReadOnlyList<LiveSession> StillLettingGo()
    {
        leaving.RemoveAll(HasLetGo);

        return [.. leaving];
    }

    private static bool HasLetGo(LiveSession gone) => gone.Life.IsCompleted && gone.Reception.Life.IsCompleted;

    private LiveSession Expected(LiveSessionKey key)
    {
        LiveSession raised;

        lock (gate)
        {
            if (sessions.TryGetValue(key, out LiveSession? running) && running.Expect())
            {
                return running;
            }

            raised = new LiveSession(
                key,
                fanouts,
                settings,
                transcoding,
                Receiving(key.Network, key.Service),
                transcoders,
                clock,
                Forget);

            sessions[key] = raised;
            raised.Expect();
            raised.Start();
        }

        events.Signal(AppEventName.Live);

        return raised;
    }

    /// <summary>
    /// The reading of this channel, raised if this is the first profile being made from it.
    /// </summary>
    private LiveReception Receiving(NetworkId network, ServiceId service)
    {
        (NetworkId Network, ServiceId Service) channel = (network, service);

        if (receptions.TryGetValue(channel, out LiveReception? reading) && reading.Attach())
        {
            return reading;
        }

        LiveReception raised = new(network, service, supply, settings, clock, Forget);

        receptions[channel] = raised;
        raised.Attach();

        return raised;
    }

    private void Forget(LiveReception reception)
    {
        lock (gate)
        {
            (NetworkId Network, ServiceId Service) channel = (reception.Network, reception.Service);

            if (receptions.TryGetValue(channel, out LiveReception? held) && ReferenceEquals(held, reception))
            {
                receptions.Remove(channel);
            }
        }
    }

    private void Forget(LiveSession session)
    {
        bool forgotten;

        lock (gate)
        {
            forgotten = sessions.TryGetValue(session.Key, out LiveSession? held)
                        && ReferenceEquals(held, session)
                        && sessions.Remove(session.Key);

            leaving.RemoveAll(HasLetGo);

            if (forgotten)
            {
                leaving.Add(session);
            }
        }

        if (forgotten)
        {
            events.Signal(AppEventName.Live);
        }
    }
}
