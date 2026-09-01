using System.Net;
using System.Net.Http.Headers;

using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldFrames : IScrubFrames
{
    private readonly ScrubFrame answer;

    public HeldFrames(ScrubFrame answer) => this.answer = answer;

    public List<TimeSpan> AskedAt { get; } = [];

    public List<RecordingId> AskedAbout { get; } = [];

    public Task<ScrubFrame> AtAsync(RecordingId id, TimeSpan at, CancellationToken cancellationToken)
    {
        AskedAbout.Add(id);
        AskedAt.Add(at);

        return Task.FromResult(answer);
    }
}

internal sealed class ScrubFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();

    public ScrubFeature(ScrubFrame answer)
    {
        Frames = new HeldFrames(answer);

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IScrubFrames>();
                services.AddSingleton<IScrubFrames>(Frames);
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Stranger = configured.WithTestScheme().CreateClient();
    }

    public HeldFrames Frames { get; }

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public static Uri For(RecordingId id, string? at = null)
        => new(
            at is null ? $"/api/videos/{id.Wire}/scrub" : $"/api/videos/{id.Wire}/scrub?at={at}",
            UriKind.Relative);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
    }
}

[Collection(FeatureTestCollection.Name)]
public sealed class ScrubDeliveryTests
{
    private static readonly byte[] Picture = [0xff, 0xd8, 0xff, 0xdb, 0x00, 0x43];

    [Fact]
    public async Task APictureOutOfARecordingIsContentAndIsRefusedToACallerWithNoSession()
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));

        using HttpResponseMessage answer = await feature.Stranger.GetAsync(
            ScrubFeature.For(RecordingId.New(), "30"));

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(feature.Frames.AskedAbout);
    }

    [Fact]
    public async Task ARefusedCallerIsToldSoRatherThanSentToASignInScreen()
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));

        using var asking = new HttpRequestMessage(HttpMethod.Get, ScrubFeature.For(RecordingId.New()));
        asking.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));

        using HttpResponseMessage answer = await feature.Stranger.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Fact]
    public async Task AFrameComesBackAsOnePictureAndNothingWrappedAroundIt()
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));
        RecordingId id = RecordingId.New();

        using HttpResponseMessage answer = await feature.Client.GetAsync(ScrubFeature.For(id, "90.5"));
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("image/jpeg", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Picture, body);
        Assert.Equal(id, Assert.Single(feature.Frames.AskedAbout));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("90.5", 90.5)]
    [InlineData("7200", 7200)]
    public async Task ThePositionAskedForIsThePositionTheFrameIsTakenAt(string at, double expected)
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));

        using HttpResponseMessage answer = await feature.Client.GetAsync(ScrubFeature.For(RecordingId.New(), at));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(expected), Assert.Single(feature.Frames.AskedAt));
    }

    [Fact]
    public async Task AskingWithoutAPositionAsksForTheStart()
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));

        using HttpResponseMessage answer = await feature.Client.GetAsync(ScrubFeature.For(RecordingId.New()));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(TimeSpan.Zero, Assert.Single(feature.Frames.AskedAt));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("later")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public async Task APositionNoRecordingCouldHaveIsRefusedBeforeAnythingIsRun(string at)
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));

        using HttpResponseMessage answer = await feature.Client.GetAsync(ScrubFeature.For(RecordingId.New(), at));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Frames.AskedAbout);
    }

    [Fact]
    public async Task SomethingThatIsNotARecordingIdIsRefusedBeforeAnythingIsRun()
    {
        await using var feature = new ScrubFeature(ScrubFrame.Of(Picture));

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri("/api/videos/not-an-id/scrub", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(feature.Frames.AskedAbout);
    }

    [Theory]
    [InlineData(ScrubRefusal.NoSuchRecording, HttpStatusCode.NotFound)]
    [InlineData(ScrubRefusal.StillBeingWritten, HttpStatusCode.Conflict)]
    [InlineData(ScrubRefusal.SourceOutOfReach, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ScrubRefusal.NothingWasDrawn, HttpStatusCode.NotFound)]
    public async Task EveryReasonThereIsNoFrameIsItsOwnAnswer(ScrubRefusal refusal, HttpStatusCode expected)
    {
        await using var feature = new ScrubFeature(ScrubFrame.Refused(refusal));

        using HttpResponseMessage answer = await feature.Client.GetAsync(ScrubFeature.For(RecordingId.New(), "30"));

        Assert.Equal(expected, answer.StatusCode);
        Assert.Equal(0, answer.Content.Headers.ContentLength ?? 0);
    }
}
