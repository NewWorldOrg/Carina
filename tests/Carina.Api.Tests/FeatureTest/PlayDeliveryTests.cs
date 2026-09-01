using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldViewing(OnTheFlyStanding standing, byte[] picture) : IOnTheFlyViewing
{
    public OnTheFlyStanding Standing { get; } = standing;

    public Stream Output { get; } = new MemoryStream(picture);

    public Task<TranscoderExit> Completion { get; } = Task.FromResult(TranscoderExit.Finished());

    public bool WasLetGo { get; private set; }

    public ValueTask DisposeAsync()
    {
        WasLetGo = true;
        Output.Dispose();

        return ValueTask.CompletedTask;
    }
}

internal sealed class HeldOnTheFlyPlayer : IOnTheFlyPlayer
{
    public byte[] Picture { get; set; } = [.. Enumerable.Range(0, 3_000).Select(index => (byte)(index % 251))];

    public OnTheFlyRefusal? Refuses { get; set; }

    public int Running { get; set; } = 1;

    public int AtOnce { get; set; } = 2;

    public TimeSpan Waited { get; set; } = TimeSpan.FromMilliseconds(138);

    public List<TimeSpan> AskedFrom { get; } = [];

    public List<string> AskedFor { get; } = [];

    public HeldViewing? Handed { get; private set; }

    public Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        TimeSpan from,
        LiveProfile profile,
        CancellationToken cancellationToken)
    {
        AskedFrom.Add(from);
        AskedFor.Add(profile.Name);

        if (Refuses is { } refusal)
        {
            return Task.FromResult(OnTheFlyStart.Refused(refusal, "the held player was told to refuse."));
        }

        Handed = new HeldViewing(
            new OnTheFlyStanding(
                from,
                Waited,
                profile,
                LiveEncoderChoice.Asked(LiveEncoder.Software),
                attributesWereMeasured: true,
                Running,
                AtOnce),
            Picture);

        return Task.FromResult(OnTheFlyStart.Started(Handed));
    }
}

internal sealed class PlayFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-play-");

    public PlayFeature()
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IOnTheFlyPlayer>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton<IOnTheFlyPlayer>(Player);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Stranger = configured.WithTestScheme().CreateClient();
    }

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public HeldRecordings Recordings { get; } = new();

    public HeldOnTheFlyPlayer Player { get; } = new();

    public string MountedAt => mounted.FullName;

    public Recording Ended(RecordingOutcome outcome, int bytes = 4_000, bool onDisk = true)
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));

        if (outcome is RecordingOutcome.Complete)
        {
            recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        }
        else
        {
            recording.Note(new OutcomeDetail(
                RecordingFault.DriverLost,
                null,
                string.Empty,
                RecordingFeature.Noon.AddMinutes(20)));
        }

        recording.Settle(outcome, bytes, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);

        if (onDisk)
        {
            File.WriteAllBytes(
                Path.Combine(mounted.FullName, recording.FileName.Value),
                [.. Enumerable.Range(0, bytes).Select(index => (byte)(index % 251))]);
        }

        return recording;
    }

    public Recording StillWriting()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), new byte[1_000]);

        return recording;
    }

    public Task<HttpResponseMessage> PlanAsync(Recording recording, string query = "")
        => AskAsync(recording, query, PlayDelivery.Json);

    public Task<HttpResponseMessage> PictureAsync(Recording recording, string query = "")
        => AskAsync(recording, query, "*/*");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
        mounted.Delete(recursive: true);
    }

    public static async Task<JsonElement> PlanOfAsync(HttpResponseMessage answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> AskAsync(Recording recording, string query, string accepting)
    {
        ArgumentNullException.ThrowIfNull(recording);

        using var asking = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/api/videos/{recording.Id.Wire}/play{query}", UriKind.Relative));

        asking.Headers.TryAddWithoutValidation("Accept", accepting);

        return await Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
    }
}

[Collection(FeatureTestCollection.Name)]
public sealed class PlayDeliveryTests
{
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
    public async Task ARecordingWhoseFileIsGoneSaysSoRatherThanStartingATranscoder()
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PlanAsync(
            feature.Ended(RecordingOutcome.Complete, onDisk: false));

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
    [InlineData("", "720p30")]
    public async Task AProfileIsOneOfTheFewThereAre(string query, string chosen)
    {
        await using var feature = new PlayFeature();

        using HttpResponseMessage answer = await feature.PictureAsync(feature.Ended(RecordingOutcome.Complete), query);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(chosen, Assert.Single(feature.Player.AskedFor));
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

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;
}
