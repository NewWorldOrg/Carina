using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Support;

internal sealed class SeatingAt(ILiveWireSource source) : ILiveSessionManager
{
    private readonly Lock gate = new();

    private readonly List<LiveSessionKey> asked = [];

    private readonly List<LiveChannelKey> reached = [];

    private readonly List<Subject> seated = [];

    private readonly List<(LiveSessionKey Key, Subject Viewer)> startedAhead = [];

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

    public IReadOnlyList<Subject> Seated
    {
        get
        {
            lock (gate)
            {
                return [.. seated];
            }
        }
    }

    public IReadOnlyList<(LiveSessionKey Key, Subject Viewer)> StartedAhead
    {
        get
        {
            lock (gate)
            {
                return [.. startedAhead];
            }
        }
    }

    public LiveAhead AnsweringAhead { get; set; } = LiveAhead.Started;

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

    public LiveAhead StartAhead(LiveSessionKey key, Subject viewer)
    {
        lock (gate)
        {
            startedAhead.Add((key, viewer));
            reachHeldWhileJoining = reachesHeld > 0;
        }

        return AnsweringAhead;
    }

    public async Task<LiveJoin> JoinAsync(LiveSessionKey key, Subject viewer, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            asked.Add(key);
            seated.Add(viewer);
            reachHeldWhileJoining = reachesHeld > 0;
        }

        return await source.JoinAsync(cancellationToken) is { } viewing
            ? LiveJoin.Joined(viewing)
            : LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, "what was being sent ended before a viewer could be seated.");
    }
}
