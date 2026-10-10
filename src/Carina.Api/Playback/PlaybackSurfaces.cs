using Carina.Api.OpenApi;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

public static class PlaybackSurfaces
{
    public const string Tag = "videos";

    public const string PlayingIsCalled = "playVideo";

    public const string ThePictureIsCalled = "getVideoThumbnail";

    public const string TheFrameIsCalled = "getVideoScrubFrame";

    public const string TheCaptionsAreCalled = "getVideoCaptions";

    public const string TheDataBroadcastIsCalled = "getVideoDataBroadcast";

    public const string AModuleOfTheDataBroadcastIsCalled = "getVideoDataBroadcastModule";

    public const string TheDataBroadcastOfARecording =
        "The catalog of a recording's data broadcast over the source it is played from: the carousel it is entered "
        + "from, whether the broadcaster asks for it to open by itself, the path of the document it opens on, whether "
        + "versions were left out of the record to keep it within its size, every version of every module of every "
        + "download of every carousel with the seconds of that source it runs from and to and the bytes its module "
        + "answers, and every event message at the second it fires, each counted from the source's own zero the way "
        + "the captions are, so that the artefact and the recording itself give a scene the same second. A version "
        + "that began before the source's zero runs from that zero, and nothing first seen or firing after the "
        + "source's end is given. It answers 409 while the data broadcast is still being taken from the recording, "
        + "or failed with tries left, and 404 where there is none to show beside that source. Opened with the "
        + "reader's own session only, never with a ticket.";

    public const string AModuleOfTheDataBroadcast =
        "One version of one module of a recording's data broadcast, as the live side channel carries it: the byte "
        + "0x02, the carousel's tag, the module id and the version, then each resource with its path, its kind and "
        + "its bytes, to the end, so that the same reader reads both. It never changes, so it is held for a day. "
        + "It answers 404 where the recording's data broadcast is not ready or does not hold that version. Opened "
        + "with the reader's own session only, never with a ticket.";

    public const string TheCaptionsOfARecording =
        "The captions of a recording for ten minutes of the source it is played from, starting at the second "
        + "asked for: the caption already showing there first, then every change before the second the answer "
        + "covers to, each at the second of that source counted from its own zero, as a palette PNG placed on "
        + "the canvas or as the screen cleared. The plan says whether there are any to ask for: it answers 409 "
        + "while they are still being taken from the recording, and 404 where there are none to draw over that "
        + "source. Opened with the reader's own session only, never with a ticket.";

    public const string HowARecordingIsPlayedInABrowser =
        "Plays a recording. Asked with Accept: application/json it answers the plan alone - how the recording "
        + "ended, whether it is transcoded as it plays, and whether seeking is a byte range or a restart. "
        + "Asked for anything else it answers the picture itself.";

    public const string ThePictureDrawnOfARecording = "The picture drawn of a recording once it had ended.";

    public const string AFrameFromWhereTheSliderIs = "One frame taken out of a recording at the second asked for.";

    public static readonly QueryInput WhereTheDataBroadcastStarts = QueryInput.Seconds(
        DataBroadcastDelivery.Position,
        "The second of the source the catalog starts at, counted from the source's own zero, as the player's position "
        + "reads it: the versions still running there or later and the event messages firing there or later are "
        + "given, to the source's end. Seconds may be fractional, and asking for none starts at the beginning.");

    public static readonly QueryInput WhichFileTheDataBroadcastIsPlacedOn = QueryInput.OneOfThese(
        DataBroadcastDelivery.Source,
        "Which of the two files the data broadcast is placed on, asked the way the plan was asked, as the captions "
        + "are. The seconds of the artefact and of the recording itself differ by what the encode skipped at the head.",
        PlaybackSources.Names,
        PlaybackSources.ArtefactIsCalled);

    public static readonly QueryInput WhatThisBrowserDecodesForItsDataBroadcast = QueryInput.SomeOfThese(
        DataBroadcastDelivery.Decodes,
        "The picture codings the browser says it decodes, named as the plan was asked with, so that the data "
        + "broadcast is placed on the file the plan settled on: naming h265 lets that be an artefact encoded in "
        + "H.265 and tagged hvc1.",
        AskedDecoding.Names);

    public static readonly QueryInput WhereTheFrameIsTakenFrom = QueryInput.Seconds(
        ScrubDelivery.Position,
        "The second of the recording the frame is taken from, counted from where the recording begins. "
        + "Seconds may be fractional, and asking for none takes the first frame.");

    public static readonly QueryInput WhereTheCaptionsStart = QueryInput.Seconds(
        CaptionDelivery.Position,
        "The second of the source the ten minutes of captions start at, counted from the source's own zero, as "
        + "the player's position reads it. Seconds may be fractional, and asking for none starts at the beginning.");

    public static readonly QueryInput WhichFileTheCaptionsArePlacedOn = QueryInput.OneOfThese(
        CaptionDelivery.Source,
        "Which of the two files the captions are placed on, asked the way the plan was asked. The seconds of the "
        + "artefact and of the recording itself differ by what the encode skipped at the head, so a caption is "
        + "placed on the one being played.",
        PlaybackSources.Names,
        PlaybackSources.ArtefactIsCalled);

    public static readonly QueryInput WhereThePlayingStarts = QueryInput.SecondsWithNoFixedDefault(
        PlayDelivery.Position,
        "The second of the recording playing starts at, counted from where the recording begins. "
        + "Seconds may be fractional. Asking for none starts where this reader last left this recording, and "
        + "at the beginning where they have not watched it before; asking for zero starts at the beginning "
        + "whatever was left. It moves the picture only where the recording is transcoded as it plays; one "
        + "handed over as it is, is seeked by a byte range, and the plan says where the watching got to so "
        + "that a player can seek there itself.");

    public static readonly QueryInput WhichProfileThePictureIsEncodedIn = QueryInput.OneOfThese(
        PlayDelivery.Quality,
        "The profile the picture is encoded in while it is transcoded as it plays. Asking for none opens at "
        + "what this machine encodes at, which depends on the encoder it has and so has no fixed default here; "
        + "GET /api/live/profiles names it, marked as the unasked one, and the answer is the same for a "
        + "recording as it is for a live channel.",
        [.. LiveProfile.All.Select(profile => profile.Name)],
        null);

    public static readonly QueryInput WhichSoundIsCarried = QueryInput.OneOfThese(
        PlayDelivery.Sound,
        "The sound carried with the picture while the recording is transcoded as it plays. Asking for none "
        + "carries the main sound, as it always did. The plan names the sounds this recording can be asked "
        + "for; one handed over as it is names none, because it carries the one sound it was encoded with.",
        SoundTracks.Names,
        SoundTracks.MainIsCalled);

    public static readonly QueryInput WhichOfTheTwoFilesIsPlayed = QueryInput.OneOfThese(
        PlayDelivery.Source,
        "Which of the two files a recording can be played from is played. Asking for the artefact hands over "
        + "the one encoded of this recording where there is one the browser plays - H.264, or H.265 tagged hvc1 "
        + "where the browser says it decodes h265 - and transcodes the recording itself while playing where there "
        + "is not; asking for none does the same, as it always did. Asking "
        + "for the recording transcodes the recording itself while playing even where an artefact was made of "
        + "it, and is refused where the recording is no longer on the disk rather than quietly handing over "
        + "the artefact. Either way the transcoder is shared with live channels, so a recording asked for as "
        + "it was recorded takes one of the few pictures this machine transcodes at once. The plan names the "
        + "one it plays and the other one it could be asked for, and apart from both the files an external "
        + "player is handed, which no browser's decoding narrows.",
        PlaybackSources.Names,
        PlaybackSources.ArtefactIsCalled);

    public static readonly QueryInput WhatThisBrowserDecodes = QueryInput.SomeOfThese(
        PlayDelivery.Decodes,
        "The picture codings the browser asking says it decodes, each named once. H.264 is always taken to be "
        + "decoded. Naming h265 lets an artefact encoded in H.265 and tagged hvc1 be handed over as it is; one "
        + "tagged hev1 is still transcoded while playing. Naming none decodes H.264 alone, as it always did, and "
        + "naming anything else is refused. The picture and the captions are asked for with what the plan was "
        + "asked with, so that all three settle on the same file.",
        AskedDecoding.Names);

    public static readonly QueryInput WhatThisBrowserDecodesForItsCaptions = QueryInput.SomeOfThese(
        CaptionDelivery.Decodes,
        "The picture codings the browser says it decodes, named as the plan was asked with, so that the captions "
        + "are placed on the file the plan settled on: naming h265 lets that be an artefact encoded in H.265 and "
        + "tagged hvc1.",
        AskedDecoding.Names);
}
