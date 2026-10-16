using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SeatingAt(ILiveWireSource source) : ILiveSessionManager
{
    private readonly Lock gate = new();

    private readonly List<LiveSessionKey> asked = [];

    private readonly List<LiveChannelKey> reached = [];

    private int reachesHeld;

    private int reachesLetGo;

    private bool reachHeldWhileJoining;

    public IReadOnlyList<LiveSessionKey> Asked
    {
        get
        {
            lock (gate)
            {
                return [.. asked];
            }
        }
    }

    public IReadOnlyList<LiveChannelKey> Reached
    {
        get
        {
            lock (gate)
            {
                return [.. reached];
            }
        }
    }

    public int ReachesLetGo => Volatile.Read(ref reachesLetGo);

    public bool HoldsAReach
    {
        get
        {
            lock (gate)
            {
                return reachesHeld > 0;
            }
        }
    }

    public bool ReachHeldWhileJoining => Volatile.Read(ref reachHeldWhileJoining);

    public Task<LiveHandover> HandOverAsync(LiveChannelKey channel, CancellationToken cancellationToken)
        => Task.FromResult(LiveHandover.Refused(LiveRefusal.DriverUnavailable));

    public IDisposable Reach(LiveChannelKey channel)
    {
        lock (gate)
        {
            reached.Add(channel);
            reachesHeld++;
        }

        return new Reaching(this);
    }

    private void LetGoOfAReach()
    {
        lock (gate)
        {
            reachesHeld--;
            reachesLetGo++;
        }
    }

    private sealed class Reaching(SeatingAt seating) : IDisposable
    {
        private int letGo;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref letGo, 1) is 0)
            {
                seating.LetGoOfAReach();
            }
        }
    }

    public async Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            asked.Add(key);
            reachHeldWhileJoining = reachesHeld > 0;
        }

        return await source.JoinAsync(cancellationToken) is { } viewing
            ? LiveJoin.Joined(viewing)
            : LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, "what was being sent ended before a viewer could be seated.");
    }
}
