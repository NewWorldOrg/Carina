using Carina.Api.OpenApi;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveSurfaces
{
    public const string Tag = "live";

    public const string HowALiveChannelIsWatchedThroughAPlaylist =
        "Watches a live channel through a playlist, for a browser that has no media source to append the wire to. "
        + "Once the first segment is ready it answers a playlist naming one playlist, that of a key made for this "
        + "browser under /api/hls/{key}/, which opens that playlist, its initialisation and its segments by the key "
        + "alone. The browser is seated on the session the wire would seat it on, so it takes no transcoder of its own, "
        + "and the picture is in H.264. A key nothing is asked with for thirty seconds is let go of, and the seat with "
        + "it. Opened with the reader's own session only, never with a ticket.";

    public static readonly QueryInput WhichProfileThePlaylistIsEncodedIn = QueryInput.OneOfThese(
        LiveHlsDelivery.Quality,
        "The profile the picture is encoded in. Asking for none opens at what this machine encodes at, which "
        + "GET /api/live/profiles marks as the unasked one.",
        [.. LiveProfile.All.Select(profile => profile.Name)],
        null);

    public static readonly QueryInput WhichSoundThePlaylistCarries = QueryInput.OneOfThese(
        LiveHlsDelivery.Sound,
        "The sound carried with the picture. Asking for none carries the main sound.",
        SoundTracks.Names,
        SoundTracks.MainIsCalled);
}
