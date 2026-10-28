using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;

namespace Carina.Api.Playback;

/// <summary>
/// A recording transcoded as it plays, handed to a browser through playlists: the first opened by the session, the
/// rest by the key it names.
/// </summary>
public static class RecordingHlsDelivery
{
    public const string Path = "/api/videos/{id}/hls.m3u8";

    public const string ThePlaylistIsCalled = "getVideoPlaylist";

    public const string Position = "from";

    public const string Quality = "profile";

    public const string Sound = "sound";

    public const string NothingCameInTime = "The transcoder wrote no segment of this recording in time to start a playlist.";

    public static async Task Invoke(
        HttpContext context,
        string id,
        PlaybackService playback,
        IOnTheFlyPlayer player,
        IPlaybackPositionRepository positions,
        IHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(keys);

        context.Response.Headers.CacheControl = LiveHlsDelivery.NeverCached;

        if (RecordingIdText.Read(id) is not { } recording)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, RecordingIdText.Description);

            return;
        }

        PlayDelivery.AskedPosition position = PlayDelivery.AskedPosition.Read(context.Request.Query[Position]);

        if (!position.Understood)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, PlayDelivery.ThePositionsThereAre);

            return;
        }

        AskedProfile profile = AskedProfile.Read(context.Request.Query[Quality]);

        if (profile.Answer is ProfileAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, PlayDelivery.TheProfilesThereAre);

            return;
        }

        AskedSound sound = AskedSound.Read(context.Request.Query[Sound]);

        if (sound.Answer is SoundAnswer.NotOneOfThese)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, PlayDelivery.TheSoundsThereAre);

            return;
        }

        if (SessionClaims.SubjectOf(context.User) is not { } watcher)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        TimeSpan from = position.Named
            ?? await PlayDelivery.WhereTheWatchingGotToAsync(context, recording, positions)
            ?? TimeSpan.Zero;

        ServiceResult<PlaybackOffer, PlaybackFailure> offered = await playback.OfferAsync(
            recording,
            sound.Track,
            PlaybackSource.Recording,
            PlaybackAudience.BrowserSayingNothing,
            context.RequestAborted);

        if (!offered.IsSuccess)
        {
            await RefuseAsync(context, PlaybackStatus.Of(offered.ErrorType), offered.ErrorMessage!);

            return;
        }

        PlaybackOffer offer = offered.Data!;
        PlayDelivery.SoundChoice chosen = await PlayDelivery.WhereTheSoundIsAsync(
            sound.Track,
            offer.Announced,
            offer.Handover,
            offer.Service,
            player,
            context.RequestAborted);

        if (chosen.Unreadable is { } unreadable)
        {
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, $"{PlayDelivery.TheSoundsCouldNotBeRead}: {unreadable.Note}");

            return;
        }

        if (chosen.Placement is not { } placement)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, PlayDelivery.TheRecordingDoesNotCarryThatSound);

            return;
        }

        OnTheFlyStart start = await StartAsync(context, player, offer, from, profile.Named, placement);

        if (start.Viewing is not { } transcode)
        {
            await RefuseAsync(context, PlayDelivery.Of(start.Refusal!.Value), PlayDelivery.Said(start.Refusal!.Value));

            return;
        }

        await OpenAsync(context, transcode, watcher, keys);
    }

    /// <summary>
    /// Starts the transcoder on a token the request can call off only while it starts, because the key it is handed
    /// to holds it after this request has been answered.
    /// </summary>
    private static async Task<OnTheFlyStart> StartAsync(
        HttpContext context,
        IOnTheFlyPlayer player,
        PlaybackOffer offer,
        TimeSpan from,
        LiveProfile? profile,
        SoundPlacement placement)
    {
        using CancellationTokenSource starting = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        return await player.StartAsync(offer.Handover, offer.Service, from, profile, VideoCodec.H264, placement, starting.Token);
    }

    private static async Task OpenAsync(HttpContext context, IOnTheFlyViewing transcode, Subject watcher, IHlsKeys keys)
    {
        if (keys.Issue(transcode, watcher) is not { } issued)
        {
            await transcode.DisposeAsync();
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, LiveHlsDelivery.AsManyAsAreHeld);

            return;
        }

        if (!await issued.Rendition.ReadyAsync(context.RequestAborted))
        {
            keys.LetGo(issued.Key);
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, NothingCameInTime);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = LiveHlsPlaylists.MediaType;

        await context.Response.WriteAsync(
            LiveHlsPlaylists.Multivariant(issued.Key, issued.Rendition.Profile, issued.Rendition.Window),
            context.RequestAborted);
    }

    private static async Task RefuseAsync(HttpContext context, int status, string because)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = LiveHlsDelivery.RefusalMediaType;

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
