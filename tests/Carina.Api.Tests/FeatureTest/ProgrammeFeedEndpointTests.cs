using System.Net;

using Carina.Api.Events;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ProgrammeFeedEndpointTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheFeedHandsBackOneProgrammePerLineAndSaysHowFarItGot()
    {
        await using var feature = new EpgFeature();

        feature.Programmes.Programmes.Add(Programme(1, 10));
        feature.Programmes.Programmes.Add(Programme(2, 20));

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri("/api/programs/bulk", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();
        string[] lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ProgrammeFeedStream.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"revision\"", lines[0], StringComparison.Ordinal);
        Assert.Equal("1:20", Cursor(response));
    }

    [Fact]
    public async Task AskingAgainFromTheCursorBringsBackOnlyWhatCameAfterIt()
    {
        await using var feature = new EpgFeature();

        feature.Programmes.Programmes.Add(Programme(1, 10));
        feature.Programmes.Programmes.Add(Programme(2, 20));

        using HttpResponseMessage next = await feature.Client.GetAsync(
            new Uri("/api/programs/bulk?cursor=1:10", UriKind.Relative));
        string[] lines = (await next.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
        Assert.Contains("\"eventId\":2", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AskingFromTheEndBringsBackNothingAndKeepsTheCursorWhereItWas()
    {
        await using var feature = new EpgFeature();

        feature.Programmes.Programmes.Add(Programme(1, 10));

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri("/api/programs/bulk?cursor=1:10", UriKind.Relative));

        Assert.Equal(string.Empty, (await response.Content.ReadAsStringAsync()).Trim());
        Assert.Equal("1:10", Cursor(response));
    }

    [Fact]
    public async Task ACursorFromAnEarlierGenerationIsToldToStartOver()
    {
        await using var feature = new EpgFeature();

        feature.Programmes.Programmes.Add(Programme(1, 10));

        await feature.PostAsync("/api/epg/rebuild", new { confirm = "discard-everything" });

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri("/api/programs/bulk?cursor=1:0", UriKind.Relative));

        Assert.Equal("{\"op\":\"reset\"}", (await response.Content.ReadAsStringAsync()).Trim());
        Assert.Equal("2:0", Cursor(response));
    }

    [Fact]
    public async Task ACursorThatIsNotAGenerationAndRevisionIsRefused()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.GetAsync("/api/programs/bulk?cursor=nonsense");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task MoreRowsThanTheCeilingComeBackAtTheCeiling()
    {
        await using var feature = new EpgFeature();

        for (int carried = 1; carried <= 12; carried++)
        {
            feature.Programmes.Programmes.Add(Programme(carried, carried));
        }

        using HttpResponseMessage response = await feature.Client.GetAsync(
            new Uri("/api/programs/bulk?rows=3", UriKind.Relative));
        string[] lines = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal("1:3", Cursor(response));
    }

    private static string? Cursor(HttpResponseMessage response)
        => response.Headers.TryGetValues(ProgrammeFeedStream.CursorHeader, out IEnumerable<string>? carried)
            ? carried.First()
            : null;

    private static Programme Programme(int carried, long revision)
    {
        Programme programme = Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(4), new ServiceId(1049), new EventId(carried)),
                new TransportStreamId(32_736),
                At,
                At.AddMinutes(30),
                $"programme {carried}",
                string.Empty,
                false),
            At);

        programme.MarkRevision(revision);

        return programme;
    }
}
