using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SegmentVersionFeature : IAsyncDisposable
{
    public const string Path = "/api/segments/version";

    public static readonly DateTime Trained = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentVersionFeature()
    {
        Versions.Others.Add(SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Candidate, false, SegmentModel.First, Trained, null, null, null, null));

        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<TimeProvider>(new MovingClock(Now));
            services.AddSingleton<ISegmentModelVersionRepository>(Versions);
            services.AddSingleton<IOccupancyReader>(new Unoccupied());
            services.AddSingleton<IDetectionWorklist>(Worklist);
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public HeldSegmentModelVersions Versions { get; } = new();

    public AskedWorklist Worklist { get; } = new();

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client.PatchAsync(new Uri(Path, UriKind.Relative), content);
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    internal sealed class Unoccupied : IOccupancyReader
    {
        public Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken)
            => Task.FromResult(new Occupancy(false, false, null));
    }

    /// <summary>
    /// Keeps which version each look of the detection asked a waiting recording for, and has none waiting.
    /// </summary>
    internal sealed class AskedWorklist : IDetectionWorklist
    {
        public List<int> Asked { get; } = [];

        public Task<LearningExtraction?> NextAsync(int modelVersion, IReadOnlyCollection<RecordingId> passedOver, CancellationToken cancellationToken)
        {
            Asked.Add(modelVersion);

            return Task.FromResult<LearningExtraction?>(null);
        }

        public Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(SeriesMember judged, int most, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningExtraction>>([]);

        public Task<IReadOnlyList<LearningExtraction>> RecordsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningExtraction>>([]);

        public Task<IReadOnlyList<LearningDataPart>> PartsAsync(RecordingId recording, IReadOnlyCollection<LearningDataKind> kinds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningDataPart>>([]);
    }
}

public sealed class SegmentVersionEndpointTests
{
    [Fact(DisplayName = "BR-CS-003: choosing a version by its number puts it in use chosen by hand, takes the one before out of use, and answers it as the status does")]
    public async Task ChoosingAVersionByItsNumberPutsItInUse()
    {
        await using var feature = new SegmentVersionFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync("""{"number":2}""");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["number", "state", "byHand", "trainedAt", "adoptedAt"], data.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal((2, "active", true), (data.GetProperty("number").GetInt32(), data.GetProperty("state").GetString(), data.GetProperty("byHand").GetBoolean()));
        Assert.Equal(("2026-11-01T18:00:00Z", "2026-11-08T12:00:00Z"), (data.GetProperty("trainedAt").GetString(), data.GetProperty("adoptedAt").GetString()));
        Assert.Equal((2, true, SegmentVersionFeature.Now), (feature.Versions.Active.Number, feature.Versions.Active.ByHand, feature.Versions.Active.ChosenByHandAt));
        SegmentModelVersion before = Assert.Single(feature.Versions.Others);
        Assert.Equal((1, SegmentModelVersionState.Retired, SegmentVersionFeature.Now), (before.Number, before.State, before.RetiredAt));
        Assert.Equal([1, 2], feature.Versions.Written.Select(version => version.Number));
    }

    [Fact(DisplayName = "BR-CS-003: choosing the version in use only marks it chosen by hand, and it stays in use from when it was put in use")]
    public async Task ChoosingTheVersionInUseOnlyMarksItChosen()
    {
        await using var feature = new SegmentVersionFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync("""{"number":1}""");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((1, true, "2026-10-18T00:00:00Z"), (data.GetProperty("number").GetInt32(), data.GetProperty("byHand").GetBoolean(), data.GetProperty("adoptedAt").GetString()));
        Assert.Equal([1], feature.Versions.Written.Select(version => version.Number));
        Assert.Equal(SegmentModelVersionState.Candidate, Assert.Single(feature.Versions.Others).State);
    }

    [Fact(DisplayName = "BR-CS-003: leaving the choice to learning keeps the version in use and no longer marks it chosen; with nothing chosen it writes nothing")]
    public async Task LeavingTheChoiceToLearningKeepsTheVersionInUse()
    {
        await using var feature = new SegmentVersionFeature();
        (HttpStatusCode untouched, JsonElement unchosen) = await feature.PatchAsync("""{"byHand":false}""");
        await feature.PatchAsync("""{"number":2}""");

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync("""{"byHand":false}""");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, untouched);
        Assert.Equal((1, false), (unchosen.GetProperty("data").GetProperty("number").GetInt32(), unchosen.GetProperty("data").GetProperty("byHand").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((2, "active", false), (data.GetProperty("number").GetInt32(), data.GetProperty("state").GetString(), data.GetProperty("byHand").GetBoolean()));
        Assert.Null(feature.Versions.Active.ChosenByHandAt);
        Assert.Equal([1, 2, 2], feature.Versions.Written.Select(version => version.Number));
    }

    [Fact(DisplayName = "BR-CD-008: once a version is chosen, detection in spare time looks for the recordings still waiting to be detected by it")]
    public async Task OnceAVersionIsChosenDetectionLooksForTheRecordingsWaitingForIt()
    {
        await using var feature = new SegmentVersionFeature();
        SegmentDetectionJob detection = feature.Built.Services.GetRequiredService<SegmentDetectionJob>();
        await detection.LookAsync(CancellationToken.None);

        await feature.PatchAsync("""{"number":2}""");
        await detection.LookAsync(CancellationToken.None);

        Assert.Equal([1, 2], feature.Worklist.Asked);
    }

    [Theory(DisplayName = "a change naming both a number and the choice left to learning, neither, or the choice made by hand without a number, is refused and changes nothing")]
    [InlineData("""{"number":2,"byHand":false}""")]
    [InlineData("""{"number":2,"byHand":true}""")]
    [InlineData("{}")]
    [InlineData("""{"byHand":true}""")]
    [InlineData("""{"number":null,"byHand":null}""")]
    [InlineData("null")]
    public async Task AChangeNamingBothOrNeitherIsRefused(string json)
    {
        await using var feature = new SegmentVersionFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("number", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Empty(feature.Versions.Written);
    }

    [Theory(DisplayName = "a change naming something else, or a number that is not a whole number, is refused and changes nothing")]
    [InlineData("""{"number":2,"version":2}""", "version")]
    [InlineData("""{"number":2.5}""", "number")]
    [InlineData("""{"byHand":"no"}""", "byHand")]
    public async Task AChangeNamingSomethingElseIsRefused(string json, string named)
    {
        await using var feature = new SegmentVersionFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(named, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Empty(feature.Versions.Written);
    }

    [Theory(DisplayName = "a number no version has is not found, and nothing changes")]
    [InlineData(7)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANumberNoVersionHasIsNotFound(int number)
    {
        await using var feature = new SegmentVersionFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync($$"""{"number":{{number}}}""");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal(1, feature.Versions.Active.Number);
        Assert.Empty(feature.Versions.Written);
    }

    [Fact(DisplayName = "a change sent from another origin is refused and changes nothing")]
    public async Task AChangeSentFromAnotherOriginIsRefused()
    {
        await using var feature = new SegmentVersionFeature();
        feature.Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        feature.Client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        (HttpStatusCode status, _) = await feature.PatchAsync("""{"number":2}""");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Empty(feature.Versions.Written);
    }

    [Fact(DisplayName = "a caller who has not signed in cannot choose a version")]
    public async Task ACallerWhoHasNotSignedInCannotChooseAVersion()
    {
        await using var feature = new SegmentVersionFeature();
        using HttpClient anonymous = feature.Anonymous();
        using var content = new StringContent("""{"number":2}""", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await anonymous.PatchAsync(new Uri(SegmentVersionFeature.Path, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(feature.Versions.Written);
        Assert.Equal(0, feature.Versions.Reads);
    }
}
