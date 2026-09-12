using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SeatingAt(ILiveWireSource source) : ILiveSessionManager
{
    private readonly Lock gate = new();

    private readonly List<LiveSessionKey> asked = [];

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

    public async Task<LiveJoin> JoinAsync(LiveSessionKey key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            asked.Add(key);
        }

        return await source.JoinAsync(cancellationToken) is { } viewing
            ? LiveJoin.Joined(viewing)
            : LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, "what was being sent ended before a viewer could be seated.");
    }
}
