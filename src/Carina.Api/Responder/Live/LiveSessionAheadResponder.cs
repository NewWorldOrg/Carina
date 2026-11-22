using Carina.Api.Services;
using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Live;

public sealed record LiveSessionAheadResponder(
    int NetworkId,
    int ServiceId,
    string Profile,
    VideoCodec Format,
    SoundTrack Sound,
    LiveAhead State,
    int WaitsForAViewerSeconds)
{
    public static LiveSessionAheadResponder Of(LiveSessionAhead ahead)
    {
        ArgumentNullException.ThrowIfNull(ahead);

        return new LiveSessionAheadResponder(
            ahead.Key.Network.Value,
            ahead.Key.Service.Value,
            ahead.Key.Profile.Name,
            ahead.Key.Format,
            ahead.Key.Sound,
            ahead.State,
            (int)ahead.WaitsForAViewer.TotalSeconds);
    }
}
