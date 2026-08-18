using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ProgrammeGuideEndpointTests
{
    private static readonly DateTime From = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private const string ADay = "?type=isdbT&from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z";

    [Fact]
    public async Task ADayOfOneTypeComesBackWithTheServicesItCovers()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(data.GetProperty("services").EnumerateArray());
        Assert.Equal("4-1049-1", data.GetProperty("programmes")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AProgrammeOnAnotherBroadcastTypeIsNotInThisGuide()
    {
        await using var feature = new EpgFeature([Satellite(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");

        Assert.Empty(body.GetProperty("data").GetProperty("programmes").EnumerateArray());
    }

    [Fact]
    public async Task AProgrammeOutsideTheWindowIsNotCarried()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddDays(3)));

        (_, JsonElement body) = await feature.GetAsync($"/api/programs{ADay}");

        Assert.Empty(body.GetProperty("data").GetProperty("programmes").EnumerateArray());
    }

    [Fact]
    public async Task AWindowWiderThanTwoDaysIsRefused()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs?type=isdbT&from=2026-08-18T00:00:00Z&to=2026-08-21T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGuideWithNoTypeNamedIsRefused()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync(
            "/api/programs?from=2026-08-18T00:00:00Z&to=2026-08-19T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AGuideThatHasNotChangedIsNotSentAgain()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        using HttpResponseMessage first = await feature.Client.GetAsync(
            new Uri($"/api/programs{ADay}", UriKind.Relative));
        string? tag = first.Headers.ETag?.ToString();

        Assert.NotNull(tag);

        using var asking = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/programs{ADay}", UriKind.Relative));

        asking.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(tag));

        using HttpResponseMessage again = await feature.Client.SendAsync(asking);

        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    [Fact]
    public async Task AVisitSinceTheLastReadMakesTheGuideWorthFetchingAgain()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        using HttpResponseMessage first = await feature.Client.GetAsync(
            new Uri($"/api/programs{ADay}", UriKind.Relative));

        await feature.Visits.SaveAsync(
            StreamVisit.Record(
                new NetworkId(4),
                new TransportStreamId(32_736),
                VisitOutcome.BasicOnly,
                From.AddHours(10),
                TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        using HttpResponseMessage again = await feature.Client.GetAsync(
            new Uri($"/api/programs{ADay}", UriKind.Relative));

        Assert.NotEqual(first.Headers.ETag?.ToString(), again.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task AProgrammeIsReadBackByTheNameTheGuideGaveIt()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        feature.Programmes.Programmes.Add(Programme(4, 1049, 1, From.AddHours(9)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/programs/4-1049-1");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("a programme", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ANameNobodyHoldsIsNotFound()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/4-1049-9");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task ANameThatIsNotThreeNumbersIsRefusedRatherThanLookedUp()
    {
        await using var feature = new EpgFeature([Terrestrial(4, 32_736, 1049)]);

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/not-a-programme");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    private static BroadcastStream Terrestrial(int network, int stream, int service)
        => Carrying(network, stream, service, TuningParameters.Terrestrial(22));

    private static BroadcastStream Satellite(int network, int stream, int service)
        => Carrying(network, stream, service, TuningParameters.Bs(5, new TransportStreamId(stream)));

    private static BroadcastStream Carrying(int network, int stream, int service, TuningParameters tuning)
        => new(
            new NetworkId(network),
            new TransportStreamId(stream),
            tuning,
            [new ServiceId(service)]);

    private static Programme Programme(int network, int service, int carried, DateTime startsAt)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                startsAt,
                startsAt.AddMinutes(30),
                "a programme",
                string.Empty,
                false),
            startsAt);
}
