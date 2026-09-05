using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class TicketedFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-ticketed-");

    public TicketedFeature()
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
            }));

        WebApplicationFactory<Program> served = configured.WithTestScheme();

        Client = served.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Player = served.CreateClient();
    }

    public HttpClient Client { get; }

    public HttpClient Player { get; }

    public HeldRecordings Recordings { get; } = new();

    public byte[] Written { get; } = [.. Enumerable.Range(0, 4_000).Select(index => (byte)(index % 251))];

    public Recording Ended()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, Written.Length, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), Written);

        return recording;
    }

    public Recording StillWriting()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        Recordings.Recordings.Add(recording);

        return recording;
    }

    public async Task<HttpResponseMessage> AskForATicketAsync(
        RecordingId id,
        string mediaType = "application/json")
    {
        ArgumentNullException.ThrowIfNull(id);

        using var content = new StringContent("{}", Encoding.UTF8, mediaType);

        return await Client.PostAsync(new Uri($"/api/videos/{id.Wire}/ticket", UriKind.Relative), content);
    }

    public async Task<string> TicketForAsync(RecordingId id)
    {
        using HttpResponseMessage answer = await AskForATicketAsync(id);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;
    }

    public async Task<HttpResponseMessage> AsAPlayerAsync(
        string path,
        string? ticket,
        string? range = null,
        HttpMethod? method = null)
    {
        using var asking = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (ticket is not null)
        {
            asking.Headers.TryAddWithoutValidation(
                "Authorization",
                $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"carina:{ticket}"))}");
        }

        if (range is not null)
        {
            asking.Headers.TryAddWithoutValidation("Range", range);
        }

        return await Player.SendAsync(asking);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Player.Dispose();
        await factory.DisposeAsync();
        mounted.Delete(recursive: true);
    }
}

[Collection(FeatureTestCollection.Name)]
public sealed class TicketedVideoTests
{
    [Fact]
    public async Task ATicketIsIssuedForOneRecordingAndSaysWhenItDies()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(recording.Id);
        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(43, read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!.Length);
        Assert.True(read.RootElement.GetProperty("data").TryGetProperty("lapsesAt", out _));
    }

    [Fact]
    public async Task AskingForATicketWithAFormRatherThanJsonIsRefused()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(
            recording.Id,
            "application/x-www-form-urlencoded");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answer.StatusCode);
    }

    [Fact]
    public async Task NobodyWithoutASessionIsGivenATicket()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using HttpResponseMessage answer = await feature.Player.PostAsync(
            new Uri($"/api/videos/{recording.Id.Wire}/ticket", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    [Fact]
    public async Task NoTicketIsIssuedForARecordingThatIsStillBeingWritten()
    {
        await using var feature = new TicketedFeature();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(feature.StillWriting().Id);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
    }

    [Fact]
    public async Task NoTicketIsIssuedForARecordingNobodyHas()
    {
        await using var feature = new TicketedFeature();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(RecordingId.New());

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task APlayerThatAsksTheHeadersThenSeeksTwiceIsAdmittedEveryTime()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);
        string path = $"/api/videos/{recording.Id.Wire}";

        using HttpResponseMessage headers = await feature.AsAPlayerAsync(path, ticket, method: HttpMethod.Head);
        using HttpResponseMessage whole = await feature.AsAPlayerAsync(path, ticket, "bytes=0-");
        using HttpResponseMessage middle = await feature.AsAPlayerAsync(path, ticket, "bytes=1000-1999");
        using HttpResponseMessage tail = await feature.AsAPlayerAsync(path, ticket, "bytes=-500");

        Assert.Equal(HttpStatusCode.OK, headers.StatusCode);
        Assert.Equal(feature.Written.Length, headers.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.PartialContent, whole.StatusCode);
        Assert.Equal(feature.Written, await whole.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal(feature.Written[1000..2000], await middle.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, tail.StatusCode);
        Assert.Equal(feature.Written[^500..], await tail.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AGrantOpensTheOneRecordingItWasEnteredWithAndNoOther()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync($"/api/videos/{mine.Id.Wire}", ticket);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(
            $"/api/videos/{another.Id.Wire}",
            ticket);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
    }

    [Fact]
    public async Task BrLa004ATicketIssuedForOneRecordingOpensNoOtherEvenOnItsFirstUse()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(
            $"/api/videos/{another.Id.Wire}",
            ticket,
            "bytes=0-");

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.NotEqual(feature.Written, await elsewhere.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BrLa004ATicketOfferedAtTheWrongRecordingIsSpentAndOpensItsOwnNoLonger()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync($"/api/videos/{another.Id.Wire}", ticket);
        using HttpResponseMessage afterwards = await feature.AsAPlayerAsync($"/api/videos/{mine.Id.Wire}", ticket);

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, afterwards.StatusCode);
    }

    [Fact]
    public async Task APlayerCarryingNothingIsRefusedWithoutBeingSentToASignInScreen()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SomethingThatWasNeverIssuedIsRefusedBeforeAnyByteLeaves()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}",
            new string('a', 43));

        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.NotEqual(feature.Written, body);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/thumbnail")]
    [InlineData("/scrub")]
    public async Task TheTicketOpensTheBytesAndNothingElseUnderTheSamePrefix(string beneath)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{beneath}",
            ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/play")]
    [InlineData("/thumbnail")]
    [InlineData("/scrub")]
    public async Task EverySurfaceUnderThisPrefixRefusesAStrangerWithTheSameStatus(string beneath)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{beneath}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Fact]
    public async Task TheAnswerToATicketIsNeverHeldByAnythingInFront()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", ticket);

        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
        Assert.Contains("Authorization", answer.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }
}
