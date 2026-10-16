using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// The sound on air, remembering whether the channel was already being reached for when it was asked.
/// </summary>
internal sealed class SoundAskedWhileReaching(Func<bool> reaching) : ISoundOnAir
{
    private int asked;

    private int askedWhileReaching;

    public int Asked => Volatile.Read(ref asked);

    public bool AskedWhileReaching => Volatile.Read(ref askedWhileReaching) is not 0;

    public Task<AnnouncedSound> AnnouncedAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref asked);

        if (reaching())
        {
            Interlocked.Exchange(ref askedWhileReaching, 1);
        }

        return Task.FromResult(default(AnnouncedSound));
    }
}
