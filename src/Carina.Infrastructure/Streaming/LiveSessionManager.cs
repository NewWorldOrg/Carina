using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveSessionManager(
    LiveSessionSettings settings,
    LiveFanoutSettings fanouts,
    ILiveSupply supply,
    ILiveTranscoderFactory transcoders,
    TimeProvider clock) : ILiveSessionManager, IAsyncDisposable
{
    public const int Attempts = 2;

    private readonly Lock gate = new();

    private readonly Dictionary<LiveSessionKey, LiveSession> sessions = [];

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

    public async ValueTask DisposeAsync()
    {
        List<LiveSession> closing;

        lock (gate)
        {
            closing = [.. sessions.Values];
        }

        foreach (LiveSession session in closing)
        {
            session.Close();
        }

        await Task.WhenAll(closing.Select(session => session.Life));
    }

    private LiveSession Expected(LiveSessionKey key)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(key, out LiveSession? running) && running.Expect())
            {
                return running;
            }

            LiveSession raised = new(key, fanouts, settings, supply, transcoders, clock, Forget);

            sessions[key] = raised;
            raised.Expect();
            raised.Start();

            return raised;
        }
    }

    private void Forget(LiveSession session)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(session.Key, out LiveSession? held) && ReferenceEquals(held, session))
            {
                sessions.Remove(session.Key);
            }
        }
    }
}
