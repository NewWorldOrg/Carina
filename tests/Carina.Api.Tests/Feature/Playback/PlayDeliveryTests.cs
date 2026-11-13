using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SoundPlacement = Carina.Domain.Streaming.SoundPlacement;

namespace Carina.Api.Tests.Feature.Playback;

[Trait("Category", "Feature")]
public sealed class PlayDeliveryTests
{
    private const int Size = 256 * 1024;

    private const int FirstHalf = Size / 2;

    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly EncodeTimeline HalfASecondSkipped = new(TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5));

    private static readonly byte[] Written = [.. Enumerable.Range(0, Size).Select(index => (byte)(index % 251))];

    [Theory]
    [InlineData(RecordingOutcome.Complete, "whole", true)]
    [InlineData(RecordingOutcome.Truncated, "cutShort", false)]
    [InlineData(RecordingOutcome.Failed, "failed", false)]
    public async Task HowTheRecordingEndedIsPartOfEveryAnswerSoNoneOfTheThreeLooksLikeAnother(
        RecordingOutcome outcome,
        string said,
        bool whole)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(outcome);

        using HttpResponseMessage plan = await feature.PlanAsync(recording);
        JsonElement read = await PlayFeature.PlanOfAsync(plan);
        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal(said, read.GetProperty("data").GetProperty("standing").GetString());
        Assert.Equal(whole, read.GetProperty("data").GetProperty("showsAsAWholeRecording").GetBoolean());
        Assert.Equal(said, Header(picture, PlaybackHeaders.Standing));
    }

    [Fact]
    public async Task AViewerWhoNamesNoSoundIsGivenTheMainOneAsTheyAlwaysWere()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Sounds = 2;

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal([SoundPlacement.WholeStream(0)], feature.Player.AskedWith);
    }

    [Fact]
    public async Task AViewerWhoAsksForTheSecondSoundIsGivenIt()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Sounds = 2;

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal([SoundPlacement.WholeStream(1)], feature.Player.AskedWith);
    }

    [Fact]
    public async Task ARecordingOfABroadcastThatCarriedOneSoundHasNoSecondOneToAskFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Sounds = 1;

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
        Assert.Equal(
            PlayDelivery.TheRecordingDoesNotCarryThatSound,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ARecordingOfABroadcastThatCarriedTwoSoundsOfTheirOwnHasNoThirdOneToAskFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.Stereo, sounds: 2);
        feature.Player.Sounds = 2;

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=third");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
        Assert.Equal(
            PlayDelivery.TheRecordingDoesNotCarryThatSound,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Fact]
    public void TheSoundsARecordingCanBePlayedWithAreNamedInTheRefusal()
        => Assert.All(SoundTracks.Names, name => Assert.Contains(name, PlayDelivery.TheSoundsThereAre, StringComparison.Ordinal));

    [Fact]
    public async Task ARecordingWhoseSoundsCouldNotBeReadIsNotStartedOnAGuess()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.SoundsCannotBeRead = "the programme was still reading the stream";

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("Main")]
    [InlineData("1")]
    public async Task ASoundThisApplicationDoesNotCarryIsRefusedAndTheOnesItDoesAreNamed(string said)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording, $"?sound={said}");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
        Assert.Equal(
            PlayDelivery.TheSoundsThereAre,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("encoded")]
    [InlineData("Artefact")]
    [InlineData("1")]
    [InlineData("../../etc/passwd")]
    public async Task SomethingThisApplicationCannotPlayARecordingFromIsRefusedAndTheTwoItCanAreNamed(string said)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording, $"?source={said}");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.Opened);
        Assert.Equal(
            PlayDelivery.TheSourcesThereAre,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("encoded")]
    [InlineData("Artefact")]
    public async Task APlanAskedForFromSomethingThisApplicationCannotPlayARecordingFromIsRefusedTheSameWay(
        string said)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PlanAsync(recording, $"?source={said}");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal(
            PlayDelivery.TheSourcesThereAre,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("?source=recording")]
    [InlineData("?source=artefact")]
    [InlineData("")]
    public async Task ARecordingNothingHasEncodedIsTranscodedFromItselfWhicheverSourceIsAskedFor(string query)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, bytes: 2_500);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, query);
        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, query))).GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded takes one of the few pictures this machine transcodes at once, and is refused when they are all taken")]
    public async Task ARecordingAskedForAsItWasRecordedIsRefusedWhenAsManyAreBeingTranscodedAsThisMachineAllows()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        feature.Player.Refuses = OnTheFlyRefusal.TooManyAlready;

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, picture.StatusCode);
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
    }

    [Fact]
    public async Task BrEd2019AnArtefactHandedOutWholeIsNotedAsOpenedAndAPlanOpensNothing()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        string artefact = feature.Jobs.Jobs.Single().ArtefactName!.Value;

        using HttpResponseMessage plan = await feature.PlanAsync(recording);

        Assert.Null(feature.Reads.LastOpened(EncodedArtefact.Shelf, artefact));

        using HttpResponseMessage picture = await feature.PictureAsync(recording, string.Empty, "bytes=0-99");

        Assert.Equal(HttpStatusCode.PartialContent, picture.StatusCode);
        Assert.NotNull(feature.Reads.LastOpened(EncodedArtefact.Shelf, artefact));
    }

    [Fact]
    public async Task ThePlanNamesTheSoundsTheRecordingCanBeAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Sounds = 2;

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(
            ["main", "secondary"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact]
    public async Task ThePlanOfABroadcastThatCarriedOneSoundNamesOnlyTheMainOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.Sounds = 1;

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(
            ["main"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact]
    public async Task ThePlanOfARecordingWhoseSoundsCouldNotBeReadNamesTheMainSoundItPlaysRatherThanNone()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.SoundsCannotBeRead = "the programme was still reading the stream";

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(
            ["main"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact]
    public async Task ThePlanOfASecondSoundThatCouldNotBeReadIsRefusedAsThePictureOfItIs()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Player.SoundsCannotBeRead = "the programme was still reading the stream";

        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?sound=secondary");
        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, plan.StatusCode);
        Assert.Equal(picture.StatusCode, plan.StatusCode);
        Assert.Equal(
            (await PlayFeature.PlanOfAsync(picture)).GetProperty("message").GetString(),
            (await PlayFeature.PlanOfAsync(plan)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task BrPd008ARecordingWhoseBroadcastPutTwoLanguagesOnOneSoundIsPlayedWithOneOfThemInBothEars()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Player.Sounds = 1;

        using HttpResponseMessage main = await feature.PictureAsync(recording);
        using HttpResponseMessage secondary = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.OK, main.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondary.StatusCode);
        Assert.Equal(
            [
                SoundPlacement.OneChannelOf(0, SoundChannel.Left),
                SoundPlacement.OneChannelOf(0, SoundChannel.Right),
            ],
            feature.Player.AskedWith);
    }

    [Fact]
    public async Task ThePlanOfABroadcastThatPutTwoLanguagesOnOneSoundNamesBothOfThem()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Player.SoundsCannotBeRead = "the stream is never asked when the broadcast announced its sound";

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(
            ["main", "secondary"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact]
    public async Task BrPd008ARecordingWithTwoLanguagesOnTheMainSoundBesideASecondStreamIsPlayedWithAnyOfTheThree()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 2);
        feature.Player.Sounds = 2;

        using HttpResponseMessage main = await feature.PictureAsync(recording);
        using HttpResponseMessage secondary = await feature.PictureAsync(recording, "?sound=secondary");
        using HttpResponseMessage third = await feature.PictureAsync(recording, "?sound=third");

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK],
            [main.StatusCode, secondary.StatusCode, third.StatusCode]);
        Assert.Equal(
            [
                SoundPlacement.OneChannelOf(0, SoundChannel.Left),
                SoundPlacement.OneChannelOf(0, SoundChannel.Right),
                SoundPlacement.WholeStream(1),
            ],
            feature.Player.AskedWith);
    }

    [Fact]
    public async Task ThePlanOfABroadcastWithTwoLanguagesOnTheMainSoundBesideASecondStreamNamesAllThree()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 2);
        feature.Player.SoundsCannotBeRead = "the stream is never asked when the broadcast announced its sound";

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(
            ["main", "secondary", "third"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact]
    public async Task ABroadcastThatAnnouncedTwoSoundsOfTheirOwnIsPlayedByTakingTheSecondStreamItCarries()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.Stereo, sounds: 2);
        feature.Player.Sounds = 2;

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal([SoundPlacement.WholeStream(1)], feature.Player.AskedWith);
        Assert.Equal(1, feature.Player.AskedWhatItCarries);
    }

    [Fact]
    public async Task ABroadcastThatAnnouncedTwoSoundsTheRecordingDoesNotCarryIsRefusedRatherThanStarted()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.Stereo, sounds: 2);
        feature.Player.Sounds = 1;

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
        Assert.Equal(1, feature.Player.AskedWhatItCarries);
        Assert.Equal(
            PlayDelivery.TheRecordingDoesNotCarryThatSound,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task ARecordingWhoseAnnouncedSecondSoundCannotBeLookedForIsNotStartedOnAGuess()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.Stereo, sounds: 2);
        feature.Player.SoundsCannotBeRead = "the programme was still reading the stream";

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
    }

    [Fact]
    public async Task ABroadcastThatAnnouncedTheOneSoundHasNoSecondOneEvenWhereTheStreamCarriesTwo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.Stereo, sounds: 1);
        feature.Player.Sounds = 2;

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?sound=secondary");
        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedWith);
        Assert.Equal(0, feature.Player.AskedWhatItCarries);
        Assert.Equal(
            ["main"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact]
    public async Task ABroadcastThatAnnouncedItsMainSoundOnlyIsPlayedWithoutTheStreamBeingRead()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.Stereo, sounds: 1);
        feature.Player.SoundsCannotBeRead = "the stream is never asked for the main sound of an announced recording";

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal([SoundPlacement.WholeStream(0)], feature.Player.AskedWith);
        Assert.Equal(0, feature.Player.AskedWhatItCarries);
    }

    [Fact]
    public async Task ARecordingThatHasNotEndedIsNotPlayedAtAll()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PlanAsync(feature.StillWriting());

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.False((await PlayFeature.PlanOfAsync(answer)).GetProperty("status").GetBoolean());
    }

    [Fact]
    public async Task SeekingIsAStartingAgainAndTheAnswerSaysSoRatherThanPretendingToBeFast()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage plan = await feature.PlanAsync(recording);
        JsonElement read = (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?from=300");

        Assert.Equal("byStartingAgain", read.GetProperty("seeking").GetString());
        Assert.False(read.GetProperty("canSeek").GetBoolean());
        Assert.True(read.GetProperty("transcodes").GetBoolean());

        Assert.Equal("byStartingAgain", Header(picture, PlaybackHeaders.Seeking));
        Assert.Equal("false", Header(picture, PlaybackHeaders.CanSeek));
        Assert.Equal("none", Assert.Single(picture.Headers.AcceptRanges));
    }

    [Fact]
    public async Task WhereTheZeroOfTheStreamSitsInsideTheRecordingIsNamedByTheAnswer()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?from=300");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("300", Header(answer, PlaybackHeaders.StartsAt));
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(feature.Player.AskedFrom));
        Assert.Equal(recording.ServiceId, Assert.Single(feature.Player.AskedOf));
    }

    [Fact]
    public async Task WhatTheTranscoderWroteIsWhatComesBackAndItIsAnMp4()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording);
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(feature.Player.Picture, body);
        Assert.True(feature.Player.Handed!.WasLetGo);
    }

    [Fact]
    public async Task TheAnswerSaysWhatItTookToStartAndHowManyAreRunning()
    {
        await using var feature = new PlayFeature();
        feature.Player.Running = 2;
        feature.Player.AtOnce = 2;
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal("0.138", Header(answer, PlaybackHeaders.Waited));
        Assert.Equal("2", Header(answer, PlaybackHeaders.Running));
        Assert.Equal("2", Header(answer, PlaybackHeaders.AtOnce));
        Assert.Equal("720p30", Header(answer, PlaybackHeaders.Profile));
        Assert.Equal("software", Header(answer, PlaybackHeaders.Encoder));
        Assert.Equal("true", Header(answer, PlaybackHeaders.AttributesWereMeasured));
    }

    [Fact]
    public async Task WhenAsManyAreRunningAsThisMachineIsAskedToTheAnswerSaysWhyRatherThanHanging()
    {
        await using var feature = new PlayFeature();
        feature.Player.Refuses = OnTheFlyRefusal.TooManyAlready;
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        Assert.Equal("tooManyAlready", Header(answer, PlaybackHeaders.Refusal));
        Assert.Equal("whole", Header(answer, PlaybackHeaders.Standing));
    }

    [Theory]
    [InlineData(OnTheFlyRefusal.NothingToPlay, HttpStatusCode.NotFound)]
    [InlineData(OnTheFlyRefusal.TranscoderWouldNotStart, HttpStatusCode.ServiceUnavailable)]
    [InlineData(OnTheFlyRefusal.NothingCameOut, HttpStatusCode.ServiceUnavailable)]
    [InlineData(OnTheFlyRefusal.TookTooLong, HttpStatusCode.ServiceUnavailable)]
    public async Task EveryWayTheTranscoderCanRefuseHasAnAnswerOfItsOwn(
        OnTheFlyRefusal refusal,
        HttpStatusCode expected)
    {
        await using var feature = new PlayFeature();
        feature.Player.Refuses = refusal;

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete));

        Assert.Equal(expected, answer.StatusCode);
        Assert.Equal(
            System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(refusal.ToString()),
            Header(answer, PlaybackHeaders.Refusal));
    }

    [Fact]
    public async Task ARecordingOfNoBytesIsNotHandedToATranscoder()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PlanAsync(feature.Ended(RecordingOutcome.Failed, 0));

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Fact]
    public async Task ARecordingWhoseFileIsGoneWhileItsRootIsThereIsNotFoundRatherThanHandedToATranscoder()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PlanAsync(
            feature.Ended(RecordingOutcome.Complete, onDisk: false));

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Fact]
    public async Task ARecordingWhoseRootIsGoneIsOutOfReachRatherThanNotFoundAndNoTranscoderIsStarted()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        Directory.Delete(feature.MountedAt, recursive: true);

        using HttpResponseMessage answer = await feature.PlanAsync(recording);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Fact]
    public async Task ARecordingNobodyHasIsNotFound()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri($"/api/videos/{RecordingId.New().Wire}/play", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task SomethingThatIsNotARecordingIdIsRefusedBeforeAnythingIsLookedFor()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri("/api/videos/not-an-id/play", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Theory]
    [InlineData("?profile=1080p60", "1080p60")]
    [InlineData("?profile=720p60", "720p60")]
    public async Task AProfileIsOneOfTheFewThereAre(string query, string chosen)
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete), query);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(chosen, Assert.Single(feature.Player.AskedFor));
    }

    [Fact]
    public async Task AskingForNoProfileLeavesTheChoiceToWhereTheEncoderIsKnownRatherThanNamingOneHere()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Null(Assert.Single(feature.Player.AskedFor));
    }

    [Theory]
    [InlineData(LiveEncoder.Vaapi, "1080p60")]
    [InlineData(LiveEncoder.Software, "720p30")]
    public async Task ARecordingNobodyChoseAProfileForOpensAtWhatThisMachineOpensAt(
        LiveEncoder machine,
        string opened)
    {
        await using var feature = new PlayFeature();
        feature.Player.Machine = machine;

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(opened, Header(answer, PlaybackHeaders.Profile));
    }

    [Fact]
    public async Task AProfileNamedOutrightIsHonouredOnAMachineThatWouldHaveOpenedAtAnother()
    {
        await using var feature = new PlayFeature();
        feature.Player.Machine = LiveEncoder.Vaapi;

        using HttpResponseMessage answer = await feature.PictureAsync(
            feature.Ended(RecordingOutcome.Complete),
            "?profile=720p30");

        Assert.Equal("720p30", Assert.Single(feature.Player.AskedFor));
        Assert.Equal("720p30", Header(answer, PlaybackHeaders.Profile));
    }

    [Theory(DisplayName = "a recording transcoded as it plays is encoded in the format asked for, where this machine encodes it")]
    [InlineData("?format=h265", VideoCodec.H265)]
    [InlineData("?format=av1", VideoCodec.Av1)]
    [InlineData("?format=h264", VideoCodec.H264)]
    public async Task ARecordingTranscodedAsItPlaysIsEncodedInTheFormatAskedFor(string query, VideoCodec format)
    {
        await using PlayFeature feature = new(VideoCodecs.InOrder);

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete), query);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(format, Assert.Single(feature.Player.AskedIn));
    }

    [Fact(DisplayName = "a recording played without naming a format is transcoded in H.264, as it always was")]
    public async Task ARecordingPlayedWithoutNamingAFormatIsTranscodedInH264()
    {
        await using PlayFeature feature = new(VideoCodecs.InOrder);

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(VideoCodec.H264, Assert.Single(feature.Player.AskedIn));
    }

    [Theory(DisplayName = "a format off the list is refused rather than passed on")]
    [InlineData("?format=hevc")]
    [InlineData("?format=AV1")]
    [InlineData("?format=h264,h265")]
    public async Task AFormatOffTheListIsRefusedRatherThanPassedOn(string query)
    {
        await using PlayFeature feature = new(VideoCodecs.InOrder);

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete), query);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedIn);
        Assert.Equal(
            PlayDelivery.TheFormatsThereAre,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Theory(DisplayName = "a format this machine does not encode is refused, picture and plan alike, rather than turned into H.264")]
    [InlineData("h265")]
    [InlineData("av1")]
    public async Task AFormatThisMachineDoesNotEncodeIsRefusedRatherThanTurnedIntoH264(string format)
    {
        await using PlayFeature feature = new([VideoCodec.H264]);
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?format=" + format);
        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?format=" + format);

        Assert.Equal(HttpStatusCode.BadRequest, picture.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, plan.StatusCode);
        Assert.Empty(feature.Player.AskedIn);
        Assert.Contains(format, (await PlayFeature.PlanOfAsync(picture)).GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory(DisplayName = "an artefact handed over as it is does not look at the format, even one this machine does not encode")]
    [InlineData("?format=av1")]
    [InlineData("?format=h265")]
    public async Task AnArtefactHandedOverAsItIsDoesNotLookAtTheFormat(string query)
    {
        await using PlayFeature feature = new([VideoCodec.H264]);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] made = feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, query);
        using HttpResponseMessage picture = await feature.PictureAsync(recording, query);

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.False((await PlayFeature.PlanOfAsync(plan)).GetProperty("data").GetProperty("transcodes").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal(made, await picture.Content.ReadAsByteArrayAsync());
        Assert.Empty(feature.Player.AskedIn);
    }

    [Theory]
    [InlineData("?profile=4320p120")]
    [InlineData("?profile=../../etc/passwd")]
    public async Task AProfileThisApplicationDoesNotEncodeIsRefusedRatherThanPassedOn(string query)
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete), query);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFor);
        Assert.Equal(
            PlayDelivery.TheProfilesThereAre,
            (await PlayFeature.PlanOfAsync(answer)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("?from=-1")]
    [InlineData("?from=soon")]
    [InlineData("?from=NaN")]
    [InlineData("?from=Infinity")]
    public async Task APositionThatIsNotASecondIntoTheRecordingIsRefused(string query)
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete), query);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Player.AskedFrom);
    }

    [Fact]
    public async Task AClientCarryingNoCredentialsIsRefusedRatherThanSentToASignInScreen()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.Stranger.GetAsync(
            new Uri($"/api/videos/{recording.Id.Wire}/play", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task NothingHandedBackSaysWhereOnThisMachineTheFileIs()
    {
        await using var feature = new PlayFeature();
        Recording gone = feature.Ended(RecordingOutcome.Complete, onDisk: false);

        using HttpResponseMessage answer = await feature.PlanAsync(gone);
        string body = await answer.Content.ReadAsStringAsync();

        Assert.DoesNotContain(feature.MountedAt, body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            answer.Headers.Concat(answer.Content.Headers),
            header => header.Value.Any(value => value.Contains(feature.MountedAt, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NothingAboutThisAnswerMayBeHeldByAnythingInFront()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete));

        Assert.Equal(PlayDelivery.NeverCached, answer.Headers.CacheControl?.ToString());
        Assert.Contains("Accept", answer.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "a recording nobody has watched is played from its beginning")]
    public async Task ARecordingNobodyHasWatchedIsPlayedFromItsBeginning()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Player.AskedFrom));
        Assert.Equal("0", Header(answer, PlaybackHeaders.StartsAt));
    }

    [Fact(DisplayName = "playing without saying where starts where this viewer left this recording")]
    public async Task PlayingWithoutSayingWhereStartsWhereThisViewerLeftIt()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        (HttpStatusCode kept, _) = await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, kept);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(600), Assert.Single(feature.Player.AskedFrom));
        Assert.Equal("600", Header(answer, PlaybackHeaders.StartsAt));
    }

    [Fact(DisplayName = "saying where to start wins over where the watching was left")]
    public async Task SayingWhereToStartWinsOverWhereTheWatchingWasLeft()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?from=30");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(feature.Player.AskedFrom));
    }

    [Fact(DisplayName = "asking to start at the beginning is not the same as asking for nothing")]
    public async Task AskingToStartAtTheBeginningIsNotTheSameAsAskingForNothing()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 600 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?from=0");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Player.AskedFrom));
    }

    [Fact(DisplayName = "where another viewer left a recording is not where this one is taken to")]
    public async Task WhereAnotherViewerLeftARecordingIsNotWhereThisOneIsTakenTo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        feature.Positions.Positions.Add(PlaybackPosition.Reached(
            recording.Id,
            new Subject("someone else"),
            TimeSpan.FromSeconds(900),
            Noon));

        using HttpResponseMessage answer = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Player.AskedFrom));
    }

    [Fact(DisplayName = "the plan says where the watching got to, so a player that seeks itself can go there")]
    public async Task ThePlanSaysWhereTheWatchingGotTo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        using (HttpResponseMessage unwatched = await feature.PlanAsync(recording))
        {
            JsonElement read = (await PlayFeature.PlanOfAsync(unwatched)).GetProperty("data");

            Assert.Equal(JsonValueKind.Null, read.GetProperty("resumeAtSec").ValueKind);
        }

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 612.5 });

        using HttpResponseMessage watched = await feature.PlanAsync(recording);
        JsonElement plan = (await PlayFeature.PlanOfAsync(watched)).GetProperty("data");

        Assert.Equal(612.5, plan.GetProperty("resumeAtSec").GetDouble());
    }

    [Fact(DisplayName = "where the watching got to is kept once for a recording, whichever of the two it is watched from")]
    public async Task WhereTheWatchingGotToIsTheSameWhicheverOfTheTwoThePlanIsAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 618.5 });

        using HttpResponseMessage asEncoded = await feature.PlanAsync(recording, "?source=artefact");
        using HttpResponseMessage asRecorded = await feature.PlanAsync(recording, "?source=recording");
        JsonElement artefact = (await PlayFeature.PlanOfAsync(asEncoded)).GetProperty("data");
        JsonElement itself = (await PlayFeature.PlanOfAsync(asRecorded)).GetProperty("data");

        Assert.Equal("artefact", artefact.GetProperty("source").GetString());
        Assert.Equal("recording", itself.GetProperty("source").GetString());
        Assert.Equal(618.5, artefact.GetProperty("resumeAtSec").GetDouble());
        Assert.Equal(618.5, itself.GetProperty("resumeAtSec").GetDouble());
        Assert.Single(feature.Positions.Positions);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded is started where the watching got to, as it is when it is asked for encoded")]
    public async Task ARecordingAskedForAsItWasRecordedIsStartedWhereTheWatchingGotTo()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        await KeepAsync(feature.Client, recording.Id.Wire, new { positionSec = 300 });

        using HttpResponseMessage answer = await feature.PictureAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(feature.Player.AskedFrom));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
    }

    [Fact]
    public async Task AnArtefactOnTheDiskIsWhatComesBackAndNothingIsTranscodedWhilePlaying()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording);
        JsonElement read = (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.False(read.GetProperty("transcodes").GetBoolean());
        Assert.True(read.GetProperty("canSeek").GetBoolean());
        Assert.Equal("video/mp4", read.GetProperty("mediaType").GetString());
        Assert.Equal(artefact.Length, read.GetProperty("bytes").GetInt64());

        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task AnArtefactCarriesTheOneSoundItWasEncodedWithSoThePlanNamesNoneToChooseFrom()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        feature.Player.Sounds = 2;

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("sounds").EnumerateArray());
    }

    [Fact]
    public async Task AskingAnArtefactForASecondSoundIsRefusedRatherThanQuietlyGivingTheOneItHas()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.BadRequest, picture.StatusCode);
        Assert.Equal(
            PlayDelivery.NothingToChooseFrom,
            (await PlayFeature.PlanOfAsync(picture)).GetProperty("message").GetString());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task AskingAnArtefactForTheMainSoundHandsItOverAsItAlwaysWas()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=main");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "an encoded recording of a broadcast that announced two sounds still offers both of them")]
    public async Task ThePlanOfAnEncodedRecordingStillNamesTheSoundsTheBroadcastAnnounced()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Encoded(recording);
        feature.Player.SoundsCannotBeRead = "the stream is never asked when the broadcast announced its sound";

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal(
            ["main", "secondary"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
        Assert.Equal(0, feature.Player.AskedWhatItCarries);
    }

    [Fact(DisplayName = "the secondary sound of an encoded recording is transcoded from the recording, because the artefact was baked with the main one")]
    public async Task AskingAnEncodedRecordingForItsSecondSoundGoesBackToTheRecordingItself()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");
        byte[] body = await picture.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal([SoundPlacement.OneChannelOf(0, SoundChannel.Right)], feature.Player.AskedWith);
        Assert.Equal(feature.Player.Picture, body);
        Assert.NotEqual(artefact, body);
    }

    [Fact(DisplayName = "the main sound of an encoded recording is the artefact itself, whatever the broadcast announced")]
    public async Task AskingAnEncodedRecordingForItsMainSoundHandsOverTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=main");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task ThePlanOfAnEncodedRecordingAskedForItsSecondSoundIsThePlanOfATranscode()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?sound=secondary"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.True(read.GetProperty("transcodes").GetBoolean());
        Assert.False(read.GetProperty("canSeek").GetBoolean());
        Assert.Equal("byStartingAgain", read.GetProperty("seeking").GetString());
    }

    [Fact(DisplayName = "when the recording itself is out of reach, the plan of an encoded recording asked for its second sound offers the artefact and the one sound it carries")]
    public async Task ThePlanNarrowsToTheArtefactWhenTheSoundAskedForCannotBeReachedAnyMore()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(
            RecordingOutcome.Complete,
            onDisk: false,
            audio: AudioMode.DualMono,
            sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?sound=secondary");
        JsonElement read = (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.False(read.GetProperty("transcodes").GetBoolean());
        Assert.Equal(artefact.Length, read.GetProperty("bytes").GetInt64());
        Assert.Equal(
            ["main"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact(DisplayName = "a picture asked for a sound that cannot be reached is refused rather than quietly handed the artefact of another sound")]
    public async Task ThePictureOfASoundThatCannotBeReachedIsRefusedRatherThanQuietlyHandedTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(
            RecordingOutcome.Complete,
            onDisk: false,
            audio: AudioMode.DualMono,
            sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.NotEqual(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task AnArtefactIsMovedAboutByAskingForARangeOfItRatherThanByStartingAgain()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording, bytes: 2_000);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, string.Empty, "bytes=1000-1099");

        Assert.Equal(HttpStatusCode.PartialContent, picture.StatusCode);
        Assert.Equal("byRange", Header(picture, PlaybackHeaders.Seeking));
        Assert.Equal("bytes", Assert.Single(picture.Headers.AcceptRanges));
        Assert.Equal(artefact[1_000..1_100], await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task OfTwoArtefactsOfOneRecordingTheOneMadeLastIsPlayed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 700);
        byte[] later = feature.Encoded(recording, minutesLater: 90, bytes: 1_300);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(later, await picture.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "an artefact a newer one replaced is never played, even while its file is still on the disk, so a newer one a browser cannot decode as it is leaves the recording to the transcoder")]
    public async Task AnArtefactANewerOneReplacedIsNeverPlayed()
    {
        await using PlayFeature feature = new();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 700);
        feature.Encoded(recording, EncodeCodec.H265, minutesLater: 90);
        feature.Jobs.Jobs[0].Replaced(feature.Jobs.Jobs[1], RecordingFeature.Noon.AddHours(3));

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
    }

    [Fact]
    public async Task AnArtefactTheLedgerNamesAndTheDiskHasNotIsTranscodedInsteadAndSaidToHaveBeen()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, onDisk: false);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal("encodedFileGone", Header(picture, PlaybackHeaders.FellBack));
        Assert.Equal(feature.Player.Picture, await picture.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnArtefactABrowserWouldNotDecodeAsItIsIsLeftToTheTranscoder()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Null(Header(picture, PlaybackHeaders.FellBack));
    }

    [Fact(DisplayName = "an artefact made in H.264 is still played as it is after its profile was changed to H.265")]
    public async Task AnArtefactMadeInH264IsStillPlayedAsItIsAfterItsProfileWasChangedToH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H264);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");
        JsonElement asked = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=artefact"))).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=artefact");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal("artefact", read.GetProperty("source").GetString());
        Assert.Equal("recording", read.GetProperty("alternative").GetString());
        Assert.Equal("artefact", asked.GetProperty("source").GetString());
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact(DisplayName = "the plan of a recording asked for as it was recorded names the artefact made in H.264 as the other one after its profile was changed to H.265")]
    public async Task ThePlanAskedForTheRecordingNamesTheArtefactMadeInH264AfterItsProfileWasChangedToH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H264);

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=recording"))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal("artefact", read.GetProperty("alternative").GetString());
    }

    [Fact(DisplayName = "an artefact whose file is H.265 is left to the transcoder even when its profile says H.264 now")]
    public async Task AnArtefactWhoseFileIsH265IsLeftToTheTranscoderEvenWhenItsProfileSaysH264Now()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264, fileReadAs: EncodeCodec.H265);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "an artefact made in H.265 and tagged hvc1 is handed over as it is to a browser that says it decodes h265")]
    public async Task AnArtefactInH265TaggedHvc1IsHandedOverToABrowserThatSaysItDecodesH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?decodes=h265"))).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=artefact&decodes=h265");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal("artefact", read.GetProperty("source").GetString());
        Assert.Equal("recording", read.GetProperty("alternative").GetString());
        Assert.Equal(["artefact", "recording"], ExternalPlayerSources(read));
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Theory(DisplayName = "a browser that does not say it decodes h265 has an artefact made in H.265 transcoded, and the plan still names it for external players")]
    [InlineData("")]
    [InlineData("?decodes=h264")]
    public async Task ABrowserThatDoesNotSayItDecodesH265HasTheArtefactTranscoded(string query)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording, query))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
        Assert.Equal(["artefact", "recording"], ExternalPlayerSources(read));
    }

    [Fact(DisplayName = "an artefact made in H.265 and tagged hev1 is transcoded even for a browser that says it decodes h265")]
    public async Task AnArtefactInH265TaggedHev1IsTranscodedEvenForABrowserThatDecodesH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hev1");

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?decodes=h264&decodes=h265"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal(["artefact", "recording"], ExternalPlayerSources(read));
    }

    [Fact(DisplayName = "the plan of a recording with nothing encoded names only the recording itself for external players")]
    public async Task ThePlanOfARecordingWithNothingEncodedNamesOnlyTheRecordingForExternalPlayers()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(["recording"], ExternalPlayerSources(read));
    }

    [Theory(DisplayName = "a decoding that is not one of the three is refused, for the plan and for the picture alike")]
    [InlineData("?decodes=vp9")]
    [InlineData("?decodes=h265&decodes=H264")]
    public async Task ADecodingThatIsNotOneOfTheThreeIsRefused(string query)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, query);
        using HttpResponseMessage picture = await feature.PictureAsync(recording, query);

        Assert.Equal(HttpStatusCode.BadRequest, plan.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, picture.StatusCode);
        Assert.Equal(PlayDelivery.TheDecodingsThereAre, (await PlayFeature.PlanOfAsync(plan)).GetProperty("message").GetString());
    }

    [Theory(DisplayName = "a browser saying it decodes AV1 is answered, and settles on the file one naming nothing more would")]
    [InlineData("?decodes=av1", "?decodes=h264")]
    [InlineData("?decodes=h265&decodes=av1", "?decodes=h265")]
    public async Task ABrowserSayingItDecodesAv1IsAnswered(string withAv1, string without)
    {
        await using PlayFeature feature = new();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        using HttpResponseMessage asked = await feature.PlanAsync(recording, withAv1);
        using HttpResponseMessage plain = await feature.PlanAsync(recording, without);

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal(
            (await PlayFeature.PlanOfAsync(plain)).GetProperty("data").GetProperty("transcodes").GetBoolean(),
            (await PlayFeature.PlanOfAsync(asked)).GetProperty("data").GetProperty("transcodes").GetBoolean());
    }

    [Fact]
    public async Task NothingSaysAPlanFellBackWhenTheArtefactItNamesIsThere()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Null(Header(picture, PlaybackHeaders.FellBack));
    }

    [Fact(DisplayName = "the chapters that come back are placed on the clock of the artefact that is played rather than of an earlier one")]
    public async Task TheChaptersThatComeBackArePlacedOnTheClockOfTheArtefactThatIsPlayed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 700, timeline: Skipped(0.5));
        feature.Encoded(recording, minutesLater: 90, bytes: 1_300, timeline: Skipped(1.5));
        await APodAsync(feature, recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(1_300, read.GetProperty("bytes").GetInt64());
        Assert.Equal([0d, 298.5d, 358.5d], read.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()).ToArray());
    }

    [Theory(DisplayName = "with an earlier artefact in H.264 and a later one in H.265, a browser is handed the newest one it decodes, with the chapters placed on that artefact's clock")]
    [InlineData("?decodes=h265", 1_300, 298.5d)]
    [InlineData("", 700, 299.5d)]
    public async Task ABrowserIsHandedTheNewestArtefactItDecodesWithItsOwnChapters(string query, long bytes, double breakStartsAt)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264, bytes: 700, fileReadAs: EncodeCodec.H264, timeline: Skipped(0.5));
        feature.Encoded(recording, EncodeCodec.H265, minutesLater: 90, bytes: 1_300, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1", timeline: Skipped(1.5));
        await APodAsync(feature, recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording, query))).GetProperty("data");
        JsonElement[] chapters = [.. read.GetProperty("chapters").EnumerateArray()];

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal(bytes, read.GetProperty("bytes").GetInt64());
        Assert.Equal(("break", breakStartsAt), (chapters[1].GetProperty("kind").GetString(), chapters[1].GetProperty("startsAtSec").GetDouble()));
    }

    [Fact]
    public async Task ThePlanOfAnArtefactWhoseRunMarkedNothingCarriesAnEmptyListRatherThanNoField()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "A recording played as it was recorded is answered with no chapters, because the ledger holds them on the artefact's clock")]
    public async Task ThePlanOfARecordingPlayedAsItWasRecordedCarriesNoChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact]
    public async Task ThePlanOfAnEncodedRecordingAskedForItsSecondSoundCarriesNoneOfTheArtefactsChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?sound=secondary"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "a recording with an artefact asked for as it was recorded hands the recording itself to the transcoder rather than the artefact")]
    public async Task ARecordingAskedForAsItWasRecordedIsTranscodedFromTheRecordingAndNotFromTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, bytes: 3_500);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");
        byte[] body = await picture.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
        Assert.Equal(PlayFeature.Root, Assert.Single(feature.Player.Opened).Root);
        Assert.Equal(feature.Player.Picture, body);
        Assert.NotEqual(artefact, body);
    }

    [Fact(DisplayName = "the plan of a recording asked for as it was recorded says it plays the recording and names the artefact as the other one")]
    public async Task ThePlanOfARecordingAskedForAsItWasRecordedNamesTheArtefactAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=recording"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal("artefact", read.GetProperty("alternative").GetString());
        Assert.True(read.GetProperty("transcodes").GetBoolean());
        Assert.False(read.GetProperty("canSeek").GetBoolean());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("bytes").ValueKind);
    }

    [Fact(DisplayName = "the plan of an encoded recording asked for as it is says it plays the artefact and names the recording itself as the other one")]
    public async Task ThePlanOfAnEncodedRecordingNamesTheRecordingItselfAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal("artefact", read.GetProperty("source").GetString());
        Assert.Equal("recording", read.GetProperty("alternative").GetString());
        Assert.Equal(artefact.Length, read.GetProperty("bytes").GetInt64());
    }

    [Fact(DisplayName = "asking outright for the artefact is asking for what playing a recording has always given")]
    public async Task AskingOutrightForTheArtefactHandsOverTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=artefact");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact(DisplayName = "an artefact the ledger names and the disk has not is not the other one the plan offers")]
    public async Task AnArtefactTheDiskHasNotIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, onDisk: false);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "an artefact holding no bytes is not the other one the plan offers")]
    public async Task AnArtefactHoldingNoBytesIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 0);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "an artefact a browser would not decode as it is, is not the other one the plan offers")]
    public async Task AnArtefactABrowserWouldNotDecodeIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded and no longer on the disk is refused rather than quietly handed its artefact")]
    public async Task ARecordingAskedForAsItWasRecordedAndNoLongerOnTheDiskIsRefused()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, onDisk: false);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");
        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, plan.StatusCode);
        Assert.NotEqual(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
        Assert.Empty(feature.Player.Opened);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded with a second sound is not narrowed to the artefact when the recording is gone")]
    public async Task ARecordingAskedForAsItWasRecordedIsNotNarrowedToTheArtefactWhenItsSecondSoundIsAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(
            RecordingOutcome.Complete,
            onDisk: false,
            audio: AudioMode.DualMono,
            sounds: 1);
        feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?source=recording&sound=secondary");

        Assert.Equal(HttpStatusCode.NotFound, plan.StatusCode);
        Assert.Null(feature.Player.Handed);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded and holding no bytes is refused rather than quietly handed its artefact")]
    public async Task ARecordingAskedForAsItWasRecordedAndHoldingNoBytesIsRefused()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Failed, bytes: 0);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.Empty(feature.Player.Opened);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded is moved about by starting again, because it is transcoded while playing")]
    public async Task ARecordingAskedForAsItWasRecordedIsMovedAboutByStartingAgainRatherThanByARange()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, bytes: 2_000);
        feature.Encoded(recording, bytes: 2_000);

        using HttpResponseMessage picture = await feature.PictureAsync(
            recording,
            "?source=recording",
            "bytes=1000-1099");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("byStartingAgain", Header(picture, PlaybackHeaders.Seeking));
        Assert.Equal("none", Assert.Single(picture.Headers.AcceptRanges));
    }

    [Fact(DisplayName = "the chapters the ledger holds belong to the artefact, so a recording asked for as it was recorded comes back with none")]
    public async Task ARecordingAskedForAsItWasRecordedComesBackWithNoneOfTheArtefactsChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=recording"))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Theory(DisplayName = "BR-PD-009: the plan names the codec the artefact's file was read as, whatever its profile says now")]
    [InlineData(EncodeCodec.H264, EncodeCodec.H264, "h264")]
    [InlineData(EncodeCodec.H265, EncodeCodec.H264, "h264")]
    [InlineData(EncodeCodec.H265, EncodeCodec.H265, "h265")]
    [InlineData(EncodeCodec.H264, EncodeCodec.H265, "h265")]
    public async Task ThePlanNamesTheCodecTheArtefactsFileWasReadAs(EncodeCodec profile, EncodeCodec file, string named)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, profile, fileReadAs: file, taggedAs: "hvc1");

        JsonElement read = await PlanAsync(feature, recording);

        Assert.Equal(named, read.GetProperty("artefactFile").GetProperty("codec").GetString());
    }

    [Fact(DisplayName = "BR-PD-009: an artefact in H.265 a browser cannot play is still named, while the recording is transcoded")]
    public async Task AnArtefactInH265ABrowserCannotPlayIsStillNamed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        JsonElement read = await PlanAsync(feature, recording);

        Assert.True(read.GetProperty("transcodes").GetBoolean());
        Assert.Equal("h265", read.GetProperty("artefactFile").GetProperty("codec").GetString());
    }

    [Fact(DisplayName = "BR-PD-009: the artefact is named when the recording itself is asked for")]
    public async Task TheArtefactIsNamedWhenTheRecordingItselfIsAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264, fileReadAs: EncodeCodec.H264);

        JsonElement read = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal("h264", read.GetProperty("artefactFile").GetProperty("codec").GetString());
    }

    [Fact(DisplayName = "BR-PD-009: an artefact whose file could not be read is named by no codec rather than by its profile")]
    public async Task AnArtefactWhoseFileCouldNotBeReadIsNamedByNoCodec()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264);

        JsonElement read = await PlanAsync(feature, recording);

        Assert.Equal(JsonValueKind.Null, read.GetProperty("artefactFile").ValueKind);
    }

    [Fact(DisplayName = "BR-PD-009: a recording with no artefact is named by no codec")]
    public async Task ARecordingWithNoArtefactIsNamedByNoCodec()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        JsonElement read = await PlanAsync(feature, recording);

        Assert.Equal(JsonValueKind.Null, read.GetProperty("artefactFile").ValueKind);
    }

    [Fact(DisplayName = "BR-CD-001: the plan of an artefact carries the chapters the version in use detected, on the artefact's clock, a likely break with something after it offered to be skipped")]
    public async Task TheArtefactsPlanCarriesTheDetectedChaptersOnItsClock()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme, 0.98), (912.3, 1002.3, SegmentKind.Break, 0.93), (1002.3, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);
        JsonElement[] chapters = [.. plan.GetProperty("chapters").EnumerateArray()];

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(
            [(0d, 911.8d, "programme", false), (911.8d, 1001.8d, "break", true), (1001.8d, 1799.5d, "programme", false)],
            chapters.Select(Read));
    }

    [Fact(DisplayName = "BR-CD-001: the plan of the recording itself carries the chapters the version in use detected at the recording's own time")]
    public async Task TheRecordingsOwnPlanCarriesTheDetectedChaptersAtItsOwnTime()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme, 0.98), (912.3, 1002.3, SegmentKind.Break, 0.93), (1002.3, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.True(plan.GetProperty("transcodes").GetBoolean());
        Assert.Equal(
            [(0d, 912.3d, "programme", false), (912.3d, 1002.3d, "break", true), (1002.3d, 1800d, "programme", false)],
            plan.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Fact(DisplayName = "BR-CD-001: a recording with an artefact asked for as it was recorded carries the chapters at the recording's own time")]
    public async Task TheRecordingAskedForAsRecordedCarriesTheChaptersAtItsOwnTime()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal("recording", plan.GetProperty("source").GetString());
        Assert.Equal([0d, 300d, 360d], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
    }

    [Fact(DisplayName = "BR-CD-009: a break less likely than the threshold is marked but not offered to be skipped, and neither is a break the recording ends on")]
    public async Task UnlikelyOrFinalBreaksAreNotOfferedToBeSkipped()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 300, SegmentKind.Programme, 0.98),
            (300, 360, SegmentKind.Break, 0.69),
            (360, 1740, SegmentKind.Programme, 0.99),
            (1740, 1800, SegmentKind.Break, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal([false, false, false, false], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("skippable").GetBoolean()));
        Assert.Equal("break", plan.GetProperty("chapters")[1].GetProperty("kind").GetString());
    }

    [Theory(DisplayName = "BR-CD-009: the threshold the settings hold decides which breaks are offered to be skipped")]
    [InlineData(0.60, true, true)]
    [InlineData(0.90, false, true)]
    [InlineData(0.95, false, false)]
    public async Task TheThresholdTheSettingsHoldDecidesWhichBreaksAreOffered(double threshold, bool unlikely, bool likely)
    {
        await using var feature = new PlayFeature();
        await feature.SegmentSettings.SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, threshold, RecordingFeature.Noon), CancellationToken.None);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 300, SegmentKind.Programme, 0.98),
            (300, 360, SegmentKind.Break, 0.69),
            (360, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.93),
            (960, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal([false, unlikely, false, likely, false], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("skippable").GetBoolean()));
    }

    [Fact(DisplayName = "BR-CD-009: an opening and an ending are played by their own kinds on the artefact and on the recording itself, each offered to be skipped when it is likely enough and something follows it")]
    public async Task AnOpeningAndAnEndingArePlayedByTheirOwnKinds()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 60, SegmentKind.Programme, 0.98),
            (60, 150, SegmentKind.Opening, 0.92),
            (150, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.93),
            (960, 1710, SegmentKind.Programme, 0.99),
            (1710, 1800, SegmentKind.Ending, 0.95));

        JsonElement artefact = await PlanAsync(feature, recording);
        JsonElement original = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal(
            [
                (0d, 59.5d, "programme", false), (59.5d, 149.5d, "opening", true), (149.5d, 899.5d, "programme", false),
                (899.5d, 959.5d, "break", true), (959.5d, 1709.5d, "programme", false), (1709.5d, 1799.5d, "ending", false),
            ],
            artefact.GetProperty("chapters").EnumerateArray().Select(Read));
        Assert.Equal(
            [
                (0d, 60d, "programme", false), (60d, 150d, "opening", true), (150d, 900d, "programme", false),
                (900d, 960d, "break", true), (960d, 1710d, "programme", false), (1710d, 1800d, "ending", false),
            ],
            original.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Theory(DisplayName = "BR-CD-009: the threshold the settings hold decides for an opening and an ending as it does for a break, and an ending with something after it is offered to be skipped")]
    [InlineData(0.70, false, true, true)]
    [InlineData(0.60, true, true, true)]
    [InlineData(0.90, false, false, true)]
    [InlineData(0.95, false, false, false)]
    public async Task TheThresholdDecidesForAnOpeningAndAnEnding(double threshold, bool opening, bool breaks, bool ending)
    {
        await using var feature = new PlayFeature();
        await feature.SegmentSettings.SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, threshold, RecordingFeature.Noon), CancellationToken.None);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 90, SegmentKind.Opening, 0.65),
            (90, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.8),
            (960, 1680, SegmentKind.Programme, 0.99),
            (1680, 1770, SegmentKind.Ending, 0.93),
            (1770, 1800, SegmentKind.Programme, 0.9));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal(
            ["opening", "programme", "break", "programme", "ending", "programme"],
            plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("kind").GetString()));
        Assert.Equal(
            [opening, false, breaks, false, ending, false],
            plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("skippable").GetBoolean()));
    }

    [Fact(DisplayName = "BR-CS-002: a recording whose detected sequence was thrown away plays with no chapters, even with chapters its encode marked")]
    public async Task ASequenceThrownAwayPlaysWithNoChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await feature.MarkedAsync(feature.Jobs.Jobs[0], new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme), new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break), new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(300), ChapterKind.Programme));
        await DetectedAsync(feature, recording.Id, (0, 600, SegmentKind.Programme, 0.6), (600, 1800, SegmentKind.Break, 0.8));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CD-013: a recording not detected plays with no chapters, whatever its encode marked")]
    public async Task ANotDetectedRecordingPlaysWithNoChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await feature.MarkedAsync(feature.Jobs.Jobs[0], new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme), new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break), new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(300), ChapterKind.Programme));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CS-002: a detection by a version no longer in use is not played")]
    public async Task ADetectionByAnotherVersionIsNotPlayed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));
        feature.Versions.Active = HeldSegmentModelVersions.InUse(2, RecordingFeature.Noon);

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "an artefact whose timeline was not kept has no detected stretch placed on it")]
    public async Task AnArtefactWithoutATimelineHasNoDetectedChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CD-001: the plan of an artefact says how far the recording's time stands ahead of the artefact's, the head the job that made it skipped, by which its chapters were moved")]
    public async Task TheArtefactsPlanSaysHowFarTheRecordingsTimeStandsAhead()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: Skipped(0.533));
        await DetectedAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme, 0.98), (912.3, 1002.3, SegmentKind.Break, 0.93), (1002.3, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(0.533, plan.GetProperty("recordingOffsetSec").GetDouble());
        Assert.Equal([0d, 911.77d, 1001.77d], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
    }

    [Fact(DisplayName = "BR-CD-001: the plan says the head skipped by the artefact it hands over, not by a newer one it could not hand over, whatever chapters it carries")]
    public async Task ThePlanSaysTheHeadSkippedByTheArtefactItHandsOver()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: Skipped(0.533));
        feature.Encoded(recording, minutesLater: 10, onDisk: false, timeline: Skipped(1.2));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(0.533, plan.GetProperty("recordingOffsetSec").GetDouble());
    }

    [Fact(DisplayName = "BR-CD-001: the plan of the recording itself says its time is the recording's own, whether or not an artefact was made of it")]
    public async Task TheRecordingsOwnPlanSaysItsTimeIsTheRecordingsOwn()
    {
        await using var feature = new PlayFeature();
        Recording alone = feature.Ended(RecordingOutcome.Complete);
        Recording encoded = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(encoded, timeline: Skipped(0.533));
        await DetectedAsync(feature, encoded.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));

        JsonElement transcoded = await PlanAsync(feature, alone);
        JsonElement asRecorded = await PlanAsync(feature, encoded, "?source=recording");

        Assert.True(transcoded.GetProperty("transcodes").GetBoolean());
        Assert.Equal(0d, transcoded.GetProperty("recordingOffsetSec").GetDouble());
        Assert.Equal("recording", asRecorded.GetProperty("source").GetString());
        Assert.Equal(0d, asRecorded.GetProperty("recordingOffsetSec").GetDouble());
    }

    [Fact(DisplayName = "an artefact whose timeline was not kept says nothing of how far the recording's time stands ahead of its own")]
    public async Task AnArtefactWithoutATimelineSaysNoOffset()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("recordingOffsetSec").ValueKind);
    }

    [Fact(DisplayName = "BR-CS-002: chapters an encode took from a detection, or from the reader of the source, are not played on an artefact the version in use has not detected")]
    public async Task ChaptersEncodedAreNotPlayedWithoutADetection()
    {
        await using var fromADetection = new PlayFeature();
        Recording detected = fromADetection.Ended(RecordingOutcome.Complete);
        fromADetection.Encoded(detected, timeline: HalfASecondSkipped, chapters: new ChapterReading(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.03, RecordingFeature.Noon.AddHours(1), 2));
        await fromADetection.MarkedAsync(fromADetection.Jobs.Jobs[0], Marked());
        await using var fromTheReader = new PlayFeature();
        Recording read = fromTheReader.Ended(RecordingOutcome.Complete);
        fromTheReader.Encoded(read, timeline: HalfASecondSkipped, chapters: new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 0.03, RecordingFeature.Noon.AddHours(1)));
        await fromTheReader.MarkedAsync(fromTheReader.Jobs.Jobs[0], Marked());

        JsonElement fromTheSegments = await PlanAsync(fromADetection, detected);
        JsonElement fromAReader = await PlanAsync(fromTheReader, read);

        Assert.Empty(fromTheSegments.GetProperty("chapters").EnumerateArray());
        Assert.Empty(fromAReader.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CA-001: asking for the plan writes nothing: no detection is kept and no setting saved")]
    public async Task AskingForThePlanWritesNothing()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));
        int kept = feature.Detections.Keeps;

        await PlanAsync(feature, recording);
        await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal(kept, feature.Detections.Keeps);
        Assert.Equal(0, feature.SegmentSettings.Saves);
    }

    [Fact]
    public async Task APictureAlreadyBeingTranscodedKeepsFlowingAfterItsSessionIsEnded()
    {
        GatedOnTheFlyPlayer player = new(Written);
        await using AuthProbe probe = Wiring(new GatedPlaybackFiles(Written), player, out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        player.Handed.Release(FirstHalf);

        using HttpRequestMessage asking = Playing(recording);
        using HttpResponseMessage response = await probe.Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        byte[] heard = new byte[Size];

        await body.ReadExactlyAsync(heard.AsMemory(0, FirstHalf));

        session.Revoke(DateTime.UtcNow);
        player.Handed.Release(Size);

        await body.ReadExactlyAsync(heard.AsMemory(FirstHalf));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Written, heard);
    }

    [Fact]
    public async Task TheNextPictureAskedForAfterThatSessionEndedIsRefused()
    {
        await using AuthProbe probe = Wiring(new GatedPlaybackFiles(Written), new GatedOnTheFlyPlayer(Written), out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpRequestMessage asking = Playing(recording);
        using HttpResponseMessage response = await probe.Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BrPd017ThePlanOfAnArtefactWhoseCaptionShiftWasMeasuredSaysItsCaptionsAreReady()
    {
        await using CaptionFeature feature = new();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, new EncodeTimeline(CaptionFeature.FileBegins, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(600)));

        Assert.Equal("ready", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd017TheTwoClocksDisagreeingByAMillisecondPlansNoCaptionsOverTheArtefactAndStillPlansThemOverTheRecording()
    {
        await using CaptionFeature feature = new();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, new EncodeTimeline(CaptionFeature.FileBegins + TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(0.5), null, null));

        Assert.Equal("none", await feature.PlannedAsync(recording, "?source=artefact"));
        Assert.Equal("ready", await feature.PlannedAsync(recording, "?source=recording"));
    }

    [Fact]
    public async Task AnArtefactWhoseJobNeverMeasuredItsTimelineHasNoCaptionsOverIt()
    {
        await using CaptionFeature feature = new();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, null);

        Assert.Equal("none", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd017ThePlanSaysCaptionsStillBeingTakenAreComing()
    {
        await using CaptionFeature feature = new();
        Recording recording = feature.Ended();

        Assert.Equal("coming", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd017ThePlanOfARecordingWithNoCaptionsSaysThereAreNone()
    {
        await using CaptionFeature feature = new();
        Recording recording = feature.Ended();
        recording.Caption(CaptionState.Absent, null, RecordingFeature.Noon.AddHours(1));

        Assert.Equal("none", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd016CaptionsTheRowSaysAreReadyWithNoRecordOnTheShelfAreComingAndNothingIsWritten()
    {
        await using CaptionFeature feature = new();
        Recording recording = await feature.CaptionedAsync(onTheShelf: false);

        Assert.Equal("coming", await feature.PlannedAsync(recording, "?source=recording"));
        Assert.Equal(CaptionState.Ready, recording.CaptionState);
    }

    [Fact]
    public async Task WithNowhereToKeepCaptionsThereAreNone()
    {
        await using CaptionFeature feature = new(anywhereToKeepThem: false);
        Recording recording = feature.Ended();

        Assert.Equal("none", await feature.PlannedAsync(recording));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> KeepAsync(
        HttpClient client,
        string recordingId,
        object body)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/videos/{recordingId}/position", UriKind.Relative),
            body);
        string read = await response.Content.ReadAsStringAsync();

        if (!read.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(read);

        return (response.StatusCode, document.RootElement.Clone());
    }

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;

    private static EncodeTimeline Skipped(double head)
        => new(TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(head), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(head));

    /// <summary>
    /// Keeps a detection of the recording by the version in use with a pod of adverts five minutes in.
    /// </summary>
    private static async Task APodAsync(PlayFeature feature, Recording recording)
    {
        SegmentSpan[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(300), SegmentKind.Programme, 0.98),
            new(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), SegmentKind.Break, 0.93),
            new(TimeSpan.FromSeconds(360), TimeSpan.FromMinutes(30), SegmentKind.Programme, 0.99),
        ];

        await feature.Detections.KeepAsync(
            RecordingDetection.Of(
                recording.Id,
                SegmentModelVersion.FirstNumber,
                SegmentDetection.Judged(segments, TimeSpan.FromMinutes(30), []),
                2,
                RecordingFeature.Noon,
                RecordingFeature.Noon),
            CancellationToken.None);
    }

    private static string[] ExternalPlayerSources(JsonElement plan)
        => [.. plan.GetProperty("externalPlayerSources").EnumerateArray().Select(source => source.GetString()!)];

    private static async Task<JsonElement> PlanAsync(PlayFeature feature, Recording recording, string query = "")
    {
        using HttpResponseMessage plan = await feature.PlanAsync(recording, query);

        return (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");
    }

    private static (double Starts, double Ends, string Kind, bool Skippable) Read(JsonElement chapter)
        => (chapter.GetProperty("startsAtSec").GetDouble(),
            chapter.GetProperty("endsAtSec").GetDouble(),
            chapter.GetProperty("kind").GetString()!,
            chapter.GetProperty("skippable").GetBoolean());

    private static ChapterSegment[] Marked()
        =>
        [
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break),
            new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1799.5), ChapterKind.Programme),
        ];

    private static async Task DetectedAsync(PlayFeature feature, RecordingId recording, params (double Starts, double Ends, SegmentKind Kind, double Probability)[] stretches)
    {
        SegmentSpan[] segments = [.. stretches.Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Starts), TimeSpan.FromSeconds(stretch.Ends), stretch.Kind, stretch.Probability))];

        await feature.Detections.KeepAsync(
            RecordingDetection.Of(recording, SegmentModelVersion.FirstNumber, SegmentDetection.Judged(segments, TimeSpan.FromMinutes(30), []), 2, RecordingFeature.Noon, RecordingFeature.Noon),
            CancellationToken.None);
    }

    private static HttpRequestMessage Playing(Recording recording)
    {
        HttpRequestMessage asking = new(HttpMethod.Get, new Uri($"/api/videos/{recording.Id.Wire}/play", UriKind.Relative));

        asking.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        return asking;
    }

    private static AuthProbe Wiring(IPlaybackFileStore files, IOnTheFlyPlayer player, out Recording recording)
    {
        HeldRecordings recordings = new();

        recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, Size, RecordingFeature.Noon.AddMinutes(30));
        recordings.Recordings.Add(recording);

        return AuthProbe.OverHttp(services =>
        {
            services.RemoveAll<IPlaybackFileStore>();
            services.RemoveAll<IOnTheFlyPlayer>();
            services.AddSingleton<IRecordingDirectory>(recordings);
            services.AddSingleton<IEncodeJobRepository>(new HeldEncodeJobs());
            services.AddSingleton<IEncodeProfileRepository>(new HeldEncodeProfiles());
            services.AddSingleton<IPlaybackPositionRepository>(new HeldPlaybackPositions());
            services.AddSingleton(files);
            services.AddSingleton(player);
        });
    }
}
