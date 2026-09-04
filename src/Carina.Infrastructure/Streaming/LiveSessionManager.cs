using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveSessionManager(
    LiveSessionSettings settings,
    LiveFanoutSettings fanouts,
    ILiveSupply supply,
    ILiveTranscoderFactory transcoders,
    ILiveCaptionerFactory captioners,
    TimeProvider clock,
    IAppEventPublisher events) : ILiveSessionManager, ILiveSessionLedger, IAsyncDisposable
{
    public const int Attempts = 2;

    private readonly Lock gate = new();

    private readonly Dictionary<LiveSessionKey, LiveSession> sessions = [];

    private readonly Dictionary<(NetworkId Network, ServiceId Service), LiveReception> receptions = [];

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

    public IReadOnlyList<LiveSessionView> Running
    {
        get
        {
            lock (gate)
            {
                return
                [
                    .. sessions.Values.Select(session => new LiveSessionView(
                        session.Key,
                        session.Viewers,
                        session.Startup.Current ?? LiveStartup.NotStarted,
                        session.Dropped,
                        session.Queued)),
                ];
            }
        }
    }

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

        return join.Refusal is LiveRefusal.NoTunerFree && await LetGoOfWhatNobodyIsWatchingAsync(key)
            ? await SeatedAsync(key, cancellationToken)
            : join;
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

    private async Task<bool> LetGoOfWhatNobodyIsWatchingAsync(LiveSessionKey asked)
    {
        List<LiveSession> given;

        lock (gate)
        {
            given =
            [
                .. sessions.Values.Where(session => !session.Key.Equals(asked) && session.NobodyIsWatching),
            ];
        }

        foreach (LiveSession session in given)
        {
            session.Close();
        }

        await Task.WhenAll(given.Select(session => session.Life));

        // The tuner is let go of by the reading, not by the session, so the asking viewer waits
        // for the reading to finish rather than being refused by a tuner on its way out.
        await Task.WhenAll(given.Select(session => session.Reception).Distinct().Select(reading => reading.Life));

        return given.Count > 0;
    }

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
                Receiving(key.Network, key.Service),
                transcoders,
                captioners,
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

        LiveReception raised = new(network, service, supply, settings, Forget);

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
        }

        if (forgotten)
        {
            events.Signal(AppEventName.Live);
        }
    }
}
