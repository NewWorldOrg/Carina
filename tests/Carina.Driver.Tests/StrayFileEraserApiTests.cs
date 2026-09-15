using System.Net;
using System.Net.Http.Json;
using System.Text;

using Carina.Contracts;
using Carina.Driver.Recording;

namespace Carina.Driver.Tests;

public sealed class StrayFileEraserApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static string RoomOf(DriverUnderTest driver) =>
        driver.Configuration.OutputRoots!.Single().Path!;

    private static string Holding(string room, string path, int size = 188)
    {
        string full = Path.Combine(room, path);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);

        return full;
    }

    private static StrayFileErasureRequest AsFound(
        string full,
        string path,
        string outputRoot = "primary"
    )
    {
        var file = new FileInfo(full);

        return new StrayFileErasureRequest
        {
            OutputRoot = outputRoot,
            Path = path,
            SizeBytes = file.Length,
            LastWrittenAt = new DateTimeOffset(
                StrayFileStamp.Truncated(file.LastWriteTimeUtc),
                TimeSpan.Zero
            ),
        };
    }

    private static Task<HttpResponseMessage> Erase(
        HttpClient client,
        StrayFileErasureRequest request
    ) =>
        client.PostAsync(
            DriverEndpoints.StrayFiles,
            JsonContent.Create(request, DriverJson.Context.StrayFileErasureRequest),
            Soon()
        );

    [Fact]
    public async Task AFileNoRecordingOwnsIsTakenOffTheDiskByTheProcessThatOwnsTheRoot()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        string beside = Holding(room, "kept.bin");
        string stray = Holding(room, "nested/leftover.tmp");

        using HttpResponseMessage response = await Erase(
            client,
            AsFound(stray, "nested/leftover.tmp")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StrayFileErasedDto? erased = await DriverUnderTest.Read(
            response,
            DriverJson.Context.StrayFileErasedDto
        );

        Assert.NotNull(erased);
        Assert.True(erased.FileRemoved);
        Assert.Equal("nested/leftover.tmp", erased.Path);
        Assert.Equal("primary", erased.OutputRoot);
        Assert.False(File.Exists(stray));
        Assert.True(File.Exists(beside));
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("nested/../../outside.bin")]
    [InlineData("./../outside.bin")]
    [InlineData("nested//outside.bin")]
    [InlineData("nested\\..\\..\\outside.bin")]
    public async Task APathThatLeavesTheRootNeverBecomesAFileToRemove(string path)
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        Holding(room, "kept.bin");
        string outside = Path.GetFullPath(Path.Combine(room, "..", "outside.bin"));
        File.WriteAllBytes(outside, new byte[188]);

        using HttpResponseMessage response = await Erase(client, AsFound(outside, path));

        DriverProblem? problem = await DriverUnderTest.Read(
            response,
            DriverJson.Context.DriverProblem
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SessionRefusalTitles.Rejected, problem?.Title);
        Assert.DoesNotContain(room, string.Join(" ", problem!.Problems), StringComparison.Ordinal);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task APathThatStartsAtTheTopOfTheDiskIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        string held = Holding(room, "kept.bin");

        using HttpResponseMessage response = await Erase(client, AsFound(held, held));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(File.Exists(held));
    }

    [Fact]
    public async Task AFileReachedThroughALinkedDirectoryIsRefusedAndTheFileBehindItStays()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        Holding(room, "kept.bin");
        string elsewhere = Path.GetFullPath(Path.Combine(room, "..", "elsewhere"));
        string victim = Holding(elsewhere, "victim.bin");
        Directory.CreateSymbolicLink(Path.Combine(room, "link"), elsewhere);

        using HttpResponseMessage response = await Erase(client, AsFound(victim, "link/victim.bin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public async Task ALinkToAFileIsRefusedAndBothTheLinkAndTheFileStay()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        string elsewhere = Path.GetFullPath(Path.Combine(room, "..", "elsewhere"));
        string victim = Holding(elsewhere, "victim.bin");
        string alias = Path.Combine(room, "alias.bin");
        File.CreateSymbolicLink(alias, victim);

        using HttpResponseMessage response = await Erase(client, AsFound(victim, "alias.bin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(File.Exists(victim));
        Assert.NotNull(new FileInfo(alias).LinkTarget);
    }

    [Fact]
    public async Task AFileThatGrewSinceItWasFoundIsLeftWhereItIs()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string stray = Holding(RoomOf(driver), "leftover.tmp");
        StrayFileErasureRequest found = AsFound(stray, "leftover.tmp");
        await File.AppendAllTextAsync(stray, "more");

        using HttpResponseMessage response = await Erase(client, found);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.StrayFileChanged,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.Equal(192, new FileInfo(stray).Length);
    }

    [Fact]
    public async Task AFileWrittenToAgainAtTheSameSizeIsLeftWhereItIs()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string stray = Holding(RoomOf(driver), "leftover.tmp");
        StrayFileErasureRequest found = AsFound(stray, "leftover.tmp");
        File.SetLastWriteTimeUtc(stray, found.LastWrittenAt.UtcDateTime.AddMinutes(1));

        using HttpResponseMessage response = await Erase(client, found);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public async Task AFileAlreadyGoneIsAnsweredAsChangedRatherThanAsRemoved()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        Holding(room, "kept.bin");
        string stray = Holding(room, "leftover.tmp");
        StrayFileErasureRequest found = AsFound(stray, "leftover.tmp");
        File.Delete(stray);

        using HttpResponseMessage response = await Erase(client, found);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ARootThatHoldsNothingAtAllIsRefusedBecauseThatIsWhatALostMountLooksLike()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string stray = Holding(RoomOf(driver), "leftover.tmp");
        StrayFileErasureRequest found = AsFound(stray, "leftover.tmp");
        File.Delete(stray);

        using HttpResponseMessage response = await Erase(client, found);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.OutputUnavailable,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
    }

    [Fact]
    public async Task TheFileOfARecordingBeingWrittenIsNeverRemovedAsAFileNobodyOwns()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);

        using HttpResponseMessage started = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording(
                    "writing",
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    "primary",
                    "still-going"
                )
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        string held = Path.Combine(room, RecordingFile.Of("still-going"));

        using HttpResponseMessage refused = await Erase(
            client,
            AsFound(held, RecordingFile.Of("still-going"))
        );

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.RecordingInProgress,
            (await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(File.Exists(held));
    }

    [Fact]
    public async Task ARootTheDriverDoesNotDeclareIsRefused()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string stray = Holding(RoomOf(driver), "leftover.tmp");

        using HttpResponseMessage response = await Erase(
            client,
            AsFound(stray, "leftover.tmp", outputRoot: "elsewhere")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.UnknownOutputRoot,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public async Task ABodyThatIsNotTheRequestIsRefusedBeforeAnythingIsTouched()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string stray = Holding(RoomOf(driver), "leftover.tmp");

        using HttpResponseMessage response = await client.PostAsync(
            DriverEndpoints.StrayFiles,
            new StringContent("{ not the request", Encoding.UTF8, "application/json"),
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public async Task ADriverThatCanThrowAFileNobodyOwnsAwaySaysSoInItsGreeting()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Health, Soon());

        DriverHello? hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.StrayFileErasure));
    }

    [Theory]
    [InlineData("a.bin", true)]
    [InlineData("nested/a.bin", true)]
    [InlineData("../a.bin", false)]
    [InlineData("nested/../a.bin", false)]
    [InlineData("./a.bin", false)]
    [InlineData("/a.bin", false)]
    [InlineData("nested\\a.bin", false)]
    [InlineData("", false)]
    public void APlaceIsOnlyReachedInsideTheRoom(string path, bool reached)
    {
        string room = Path.Combine(Path.GetTempPath(), "carina-room");

        Assert.Equal(reached, StrayFileEraser.PlaceUnder(room, path) is not null);
    }

    [Theory]
    [InlineData("k-1.ts", "k-1")]
    [InlineData("nested/k-1.ts", null)]
    [InlineData("k-1.bin", null)]
    [InlineData(".hidden.ts", null)]
    public void OnlyAFileNamedTheWayARecordingIsNamedIsHeldForItsSession(string path, string? recordingId)
    {
        Assert.Equal(recordingId, StrayFileEraser.RecordingNamedBy(path));
    }
}
