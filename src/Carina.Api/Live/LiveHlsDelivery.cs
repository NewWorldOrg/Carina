using Carina.Api.Authentication;
using Carina.Api.Playback;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

/// <summary>
/// A live channel handed to a player that opens a playlist. The first playlist is opened by the session or by a ticket
/// and names a playlist of a key made for that player; that playlist, the initialisation and the segments are opened
/// by the key alone.
/// </summary>
public static class LiveHlsDelivery
{
    public const string Path = "/api/live/{networkId}-{serviceId}/hls.m3u8";

    public const string ThePlaylistOfAKey = "/api/hls/{key:length(43)}/index.m3u8";

    public const string TheInitialisationOfAKey = "/api/hls/{key:length(43)}/" + LiveHlsPlaylists.Initialisation;

    public const string ASegmentOfAKey = "/api/hls/{key:length(43)}/{number:long:min(0)}.m4s";

    public const string Quality = "profile";

    public const string Sound = "sound";

    public const string PictureMediaType = "video/mp4";

    public const string NeverCached = "no-store, private";

    public const string RefusalMediaType = "text/plain; charset=utf-8";

    public const string ThePlaylistIsCalled = "getLivePlaylist";

    public const string TheChannelThereIs =
        "A live channel is asked for by a network and a service, each a whole number in the range a broadcast carries.";

    public const string TheProfilesThereAre =
        "A playlist is asked for in one of the profiles this application encodes, or in the one it opens at when none is named.";

    public static readonly string TheSoundsThereAre =
        $"A playlist carries one of the sounds {string.Join(", ", SoundTracks.Names)}, or the main one when none is named.";

    public const string NotHeld = "This is opened with the key a playlist named, and no such key is held.";

    public const string NothingCameInTime = "Nothing came from this channel in time to start a playlist.";

    public const string AsManyAsAreHeld = "As many playlists are open as this application holds at once.";

    public static async Task Invoke(
        HttpContext context,
        int networkId,
        int serviceId,
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        ILiveEncoderSelector encoders,
        ILiveHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(encoders);

        context.Response.Headers.CacheControl = NeverCached;

        if (Channel(networkId, serviceId) is not { } channel)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, TheChannelThereIs);

            return;
        }

        AskedProfile profile = AskedProfile.Read(context.Request.Query[Quality]);

        if (profile.Answer is ProfileAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, TheProfilesThereAre);

            return;
        }

        AskedSound sound = AskedSound.Read(context.Request.Query[Sound]);

        if (sound.Answer is SoundAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, TheSoundsThereAre);

            return;
        }

        if (SessionClaims.SubjectOf(context.User) is not { } watcher)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        LiveProfile asked = profile.Named ?? LiveProfile.Unasked((await encoders.ChooseAsync(context.RequestAborted)).Encoder);

        await OpenAsync(
            context,
            new LiveSessionKey(channel.Network, channel.Service, asked, sound.Track),
            watcher,
            sessions,
            onAir,
            keys);
    }

    /// <summary>
    /// Seats the watcher on the session asked for, issues a key holding the seat, and answers, once the first segment
    /// has been cut, the playlist naming the key's own playlist. The picture is in H.264 whatever else this machine
    /// encodes, because a player opening a playlist is not asked what it decodes.
    /// </summary>
    public static async Task OpenAsync(
        HttpContext context,
        LiveSessionKey asked,
        Subject watcher,
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        ILiveHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(keys);

        context.Response.Headers.CacheControl = NeverCached;

        LiveSessionKey inH264 = new(asked.Network, asked.Service, asked.Profile, asked.Sound);
        (_, LiveJoin join) = await LiveSeating.JoinAsync(sessions, onAir, inH264, context.RequestAborted);

        if (join.Viewing is not { } seat)
        {
            LiveRefusal refused = join.Refusal!.Value;

            await RefuseAsync(context, StatusOf(refused), LiveRefusalClosures.Because(refused));

            return;
        }

        if (keys.Issue(seat, watcher, inH264.Profile) is not { } issued)
        {
            await seat.DisposeAsync();
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, AsManyAsAreHeld);

            return;
        }

        if (!await issued.Rendition.ReadyAsync(context.RequestAborted))
        {
            keys.LetGo(issued.Key);
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, NothingCameInTime);

            return;
        }

        await AnswerAsync(
            context,
            LiveHlsPlaylists.MediaType,
            LiveHlsPlaylists.Multivariant(issued.Key, issued.Rendition.Profile, issued.Rendition.Window));
    }

    public static Task ThePlaylist(HttpContext context, string key, ILiveHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(keys);

        context.Response.Headers.CacheControl = NeverCached;

        return keys.Find(key) is { } rendition
            ? AnswerAsync(context, LiveHlsPlaylists.MediaType, LiveHlsPlaylists.Media(rendition.Window))
            : RefuseAsync(context, StatusCodes.Status403Forbidden, NotHeld);
    }

    public static Task TheInitialisation(HttpContext context, string key, ILiveHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(keys);

        context.Response.Headers.CacheControl = NeverCached;

        if (keys.Find(key) is not { } rendition)
        {
            return RefuseAsync(context, StatusCodes.Status403Forbidden, NotHeld);
        }

        return HandAsync(context, rendition.Initialisation);
    }

    public static Task ASegment(HttpContext context, string key, long number, ILiveHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(keys);

        context.Response.Headers.CacheControl = NeverCached;

        if (keys.Find(key) is not { } rendition)
        {
            return RefuseAsync(context, StatusCodes.Status403Forbidden, NotHeld);
        }

        return HandAsync(context, rendition.Segment(number));
    }

    internal static LiveChannelKey? Channel(int networkId, int serviceId)
        => networkId is < NetworkId.MinValue or > NetworkId.MaxValue || serviceId is < ServiceId.MinValue or > ServiceId.MaxValue
            ? null
            : new LiveChannelKey(new NetworkId(networkId), new ServiceId(serviceId));

    private static int StatusOf(LiveRefusal refusal)
        => refusal is LiveRefusal.NoSuchChannel ? StatusCodes.Status404NotFound : StatusCodes.Status503ServiceUnavailable;

    private static Task HandAsync(HttpContext context, ReadOnlyMemory<byte>? held)
    {
        if (held is not { } bytes)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = PictureMediaType;
        context.Response.ContentLength = bytes.Length;

        return context.Response.Body.WriteAsync(bytes, context.RequestAborted).AsTask();
    }

    private static Task AnswerAsync(HttpContext context, string mediaType, string text)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = mediaType;

        return context.Response.WriteAsync(text, context.RequestAborted);
    }

    private static async Task RefuseAsync(HttpContext context, int status, string because)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = RefusalMediaType;

        try
        {
            await context.Response.WriteAsync(because, context.RequestAborted);
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
    }
}
