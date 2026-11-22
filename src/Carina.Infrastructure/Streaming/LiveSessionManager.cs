using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Streaming;

using Microsoft.Extensions.Logging;

using Puppis.Contracts;

using Subject = Carina.Domain.Auth.Subject;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveSessionManager(
    LiveSessionSettings settings,
    LiveFanoutSettings fanouts,
    LiveTranscodeSettings transcoding,
    ILiveSupply supply,
    ILiveTranscoderFactory transcoders,
    TimeProvider clock,
    IAppEventPublisher events,
    ILogger<LiveSessionManager> logger) : ILiveSessionManager, ILiveSessionLedger, IAsyncDisposable
{
    public const int Attempts = 2;

    private readonly Lock gate = new();

    private readonly Dictionary<LiveSessionKey, LiveSession> sessions = [];

    private readonly Dictionary<(NetworkId Network, ServiceId Service), LiveReception> receptions = [];

    private readonly List<LiveSession> leaving = [];

    private readonly Dictionary<Subject, LiveSession> ahead = [];

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

    public async Task<LiveJoin> JoinAsync(LiveSessionKey key, Subject viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(viewer);

        LiveJoin join = await SeatedAsync(key, viewer, cancellationToken);

        if (join.Refusal is LiveRefusal.TooManyAlready)
        {
            join = await SeatedAfterStoppingWhatWasStartedAheadAsync(key, viewer, join, cancellationToken);
        }

        return join.Refusal is LiveRefusal.NoTunerFree
            ? await SeatedAfterLettingGoAsync(key, viewer, join, cancellationToken)
            : join;
    }

    public LiveAhead StartAhead(LiveSessionKey key, Subject viewer)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(viewer);

        LiveSession awaiting;
        LiveSession? left;
        bool raised = false;

        lock (gate)
        {
            LiveSession? running = sessions.GetValueOrDefault(key);
            LiveAwaiting standing = running?.Await(viewer, settings.LongestWaitForAViewer) ?? LiveAwaiting.Closed;

            if (standing is LiveAwaiting.Watched)
            {
                return LiveAhead.AlreadyWatched;
            }

            if (running is null || standing is LiveAwaiting.Closed)
            {
                running = Raised(key);
                running.Await(viewer, settings.LongestWaitForAViewer);
                running.Start();
                raised = true;
            }

            awaiting = running;
            left = TurnedTo(viewer, awaiting);
        }

        if (raised)
        {
            events.Signal(AppEventName.Live);
            SayIfNothingComesOfIt(awaiting);
        }

        HandOver(left, awaiting, viewer);

        return raised ? LiveAhead.Started : LiveAhead.AlreadyRunning;
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

    public IDisposable Reach(LiveChannelKey channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        LiveReception reading;

        lock (gate)
        {
            reading = Receiving(channel.Network, channel.Service);
        }

        reading.Open();

        return new Reached(reading);
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

    private async Task<LiveJoin> SeatedAsync(LiveSessionKey key, Subject viewer, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (await Expected(key, viewer).JoinAsync(cancellationToken) is { } join)
            {
                return join;
            }
        }

        return LiveJoin.Refused(
            LiveRefusal.TranscoderWouldNotStart,
            "what the transcoder wrote ended before a viewer could be seated.");
    }

    private async Task<LiveJoin> SeatedAfterStoppingWhatWasStartedAheadAsync(
        LiveSessionKey key,
        Subject viewer,
        LiveJoin refused,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveSession> stopping = StopWhatWasStartedAhead(key);

        if (stopping.Count is 0)
        {
            return refused;
        }

        using CancellationTokenSource deadline = new(settings.LongestWaitForATunerToComeFree, clock);
        using CancellationTokenSource leash =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        return await WithinAsync(ReleasedAndEndedAsync(stopping), leash.Token, cancellationToken)
            ? await SeatedAsync(key, viewer, cancellationToken)
            : refused;
    }

    private static async Task<bool> ReleasedAndEndedAsync(IReadOnlyList<LiveSession> stopping)
    {
        await ReleasedAsync(stopping);

        return await EndedAsync(stopping);
    }

    private List<LiveSession> StopWhatWasStartedAhead(LiveSessionKey asked)
    {
        List<LiveSession> candidates;

        lock (gate)
        {
            candidates =
            [
                .. sessions.Values.Where(session => !session.Key.Equals(asked) && session.AwaitedByViewersNobodyHasSeen),
            ];
        }

        return [.. candidates.Where(session => session.CloseIfNobodyIsWatching())];
    }

    private async Task<LiveJoin> SeatedAfterLettingGoAsync(
        LiveSessionKey key,
        Subject viewer,
        LiveJoin refused,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LiveSession> letting = LetGoOfWhatNobodyIsWatching(key);

        if (letting.Count is 0)
        {
            return refused;
        }

        using CancellationTokenSource deadline = new(settings.LongestWaitForATunerToComeFree, clock);
        using CancellationTokenSource leash =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        if (!await WithinAsync(ReleasedAsync(letting), leash.Token, cancellationToken))
        {
            return refused;
        }

        LiveJoin again = await SeatedAsync(key, viewer, cancellationToken);

        if (again.Refusal is not LiveRefusal.TooManyAlready || letting.All(session => session.Life.IsCompleted))
        {
            return again;
        }

        return await WithinAsync(EndedAsync(letting), leash.Token, cancellationToken)
            ? await SeatedAsync(key, viewer, cancellationToken)
            : again;
    }

    private List<LiveSession> LetGoOfWhatNobodyIsWatching(LiveSessionKey asked)
    {
        List<LiveSession> candidates;
        List<LiveSession> going;

        lock (gate)
        {
            candidates =
            [
                .. sessions.Values.Where(session => !session.Key.Equals(asked) && session.NobodyIsWatching),
            ];
            going = [.. StillLettingGo().Where(session => !session.Key.Equals(asked))];
        }

        List<LiveSession> given = [];

        foreach (LiveSession session in candidates)
        {
            if (session.CloseIfNobodyIsWatching())
            {
                given.Add(session);
            }
        }

        return [.. given, .. going];
    }

    private static async Task<bool> WithinAsync(
        Task<bool> waiting,
        CancellationToken leash,
        CancellationToken cancellationToken)
    {
        try
        {
            return await waiting.WaitAsync(leash);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> ReleasedAsync(IReadOnlyList<LiveSession> letting)
    {
        await Task.WhenAll(letting.Select(session => session.Released));

        LiveReception[] closing =
        [
            .. letting.Select(session => session.Reception).Distinct().Where(reading => reading.IsClosed),
        ];

        await Task.WhenAll(closing.Select(reading => QuietlyAsync(reading.Life)));

        return closing.Length > 0;
    }

    private static async Task<bool> EndedAsync(IReadOnlyList<LiveSession> letting)
    {
        await Task.WhenAll(letting.Select(session => QuietlyAsync(session.Life)));

        return true;
    }

    private static async Task QuietlyAsync(Task ending)
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

    private IReadOnlyList<LiveSession> StillLettingGo()
    {
        leaving.RemoveAll(HasLetGo);

        return [.. leaving];
    }

    private static bool HasLetGo(LiveSession gone)
        => gone.Released.IsCompleted && (!gone.Reception.IsClosed || gone.Reception.Life.IsCompleted);

    private LiveSession Expected(LiveSessionKey key, Subject viewer)
    {
        LiveSession expected;
        LiveSession? left;
        bool raised = false;

        lock (gate)
        {
            if (!sessions.TryGetValue(key, out LiveSession? running) || !running.Expect())
            {
                running = Raised(key);
                running.Expect();
                running.Start();
                raised = true;
            }

            expected = running;
            left = TurnedTo(viewer, null);
        }

        if (raised)
        {
            events.Signal(AppEventName.Live);
        }

        HandOver(left, expected, viewer);

        return expected;
    }

    private LiveSession Raised(LiveSessionKey key)
    {
        LiveSession raised = new(
            key,
            fanouts,
            settings,
            transcoding,
            Receiving(key.Network, key.Service),
            transcoders,
            clock,
            logger,
            Forget);

        sessions[key] = raised;

        return raised;
    }

    private LiveSession? TurnedTo(Subject viewer, LiveSession? awaiting)
    {
        ahead.Remove(viewer, out LiveSession? before);

        if (awaiting is not null)
        {
            ahead[viewer] = awaiting;
        }

        return before;
    }

    private static void HandOver(LiveSession? from, LiveSession to, Subject viewer)
    {
        if (from is null || ReferenceEquals(from, to))
        {
            return;
        }

        _ = to.Secured.ContinueWith(
            secured =>
            {
                if (secured.Result)
                {
                    from.NoLongerAwaitedBy(viewer);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void SayIfNothingComesOfIt(LiveSession awaiting)
        => _ = awaiting.Raising.ContinueWith(
            raising => SayNothingCameOfIt(awaiting.Key, raising),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private void SayNothingCameOfIt(LiveSessionKey key, Task<LiveJoin?> raising)
    {
        if (raising.IsFaulted)
        {
            logger.LogWarning(
                raising.Exception.GetBaseException(),
                "A live session on {Key} started ahead of its viewer failed before it was raised.",
                key.ToString());

            return;
        }

        if (raising.IsCompletedSuccessfully && raising.Result is { } refused)
        {
            logger.LogInformation(
                "A live session on {Key} started ahead of its viewer was refused ({Refusal}): {Note}",
                key.ToString(),
                refused.Refusal,
                refused.Note);
        }
    }

    private LiveReception Receiving(NetworkId network, ServiceId service)
    {
        (NetworkId Network, ServiceId Service) channel = (network, service);

        if (receptions.TryGetValue(channel, out LiveReception? reading) && reading.Attach())
        {
            return reading;
        }

        LiveReception raised = new(network, service, supply, settings, clock, logger, Forget);

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

            Subject[] awaiting = [.. ahead.Where(awaited => ReferenceEquals(awaited.Value, session)).Select(awaited => awaited.Key)];

            foreach (Subject viewer in awaiting)
            {
                ahead.Remove(viewer);
            }

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

    private sealed class Reached(LiveReception reading) : IDisposable
    {
        private int letGo;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref letGo, 1) is 0)
            {
                reading.Detach();
            }
        }
    }
}
