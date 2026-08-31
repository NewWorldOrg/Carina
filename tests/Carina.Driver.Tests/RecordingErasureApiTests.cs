using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class RecordingErasureApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static string RoomOf(DriverUnderTest driver) =>
        driver.Configuration.OutputRoots!.Single().Path!;

    private static string Holding(string room, string recordingId)
    {
        string path = Path.Combine(room, RecordingFile.Of(recordingId));

        File.WriteAllBytes(path, new byte[188]);

        return path;
    }

    private static Task<HttpResponseMessage> Erase(
        HttpClient client,
        string recordingId,
        string outputRoot = "primary"
    ) =>
        client.DeleteAsync(
            $"{DriverEndpoints.Recordings}/{Uri.EscapeDataString(recordingId)}"
            + $"?{DriverEndpoints.OutputRootQuery}={Uri.EscapeDataString(outputRoot)}",
            Soon()
        );

    [Fact]
    public async Task TheProcessThatWroteTheRecordingIsTheOneThatTakesItOffTheDisk()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string held = Holding(RoomOf(driver), "kept-1");

        using HttpResponseMessage response = await Erase(client, "kept-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RecordingErasedDto? erased = await DriverUnderTest.Read(
            response,
            DriverJson.Context.RecordingErasedDto
        );

        Assert.NotNull(erased);
        Assert.Equal("kept-1", erased.RecordingId);
        Assert.True(erased.FileRemoved);
        Assert.False(File.Exists(held));
    }

    [Fact]
    public async Task NothingBesideTheRecordingAskedForIsTouched()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        Holding(room, "asked-for");
        string beside = Holding(room, "left-alone");

        using HttpResponseMessage response = await Erase(client, "asked-for");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(beside));
        Assert.Equal([Path.GetFileName(beside)], Directory.GetFiles(room).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ARecordingWhoseFileHasAlreadyGoneIsAnsweredWithoutRemovingAnything()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string beside = Holding(RoomOf(driver), "left-alone");

        using HttpResponseMessage response = await Erase(client, "never-written");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RecordingErasedDto? erased = await DriverUnderTest.Read(
            response,
            DriverJson.Context.RecordingErasedDto
        );

        Assert.NotNull(erased);
        Assert.False(erased.FileRemoved);
        Assert.True(File.Exists(beside));
    }

    [Theory]
    [InlineData("down/away")]
    [InlineData("../away")]
    [InlineData("down/../../away")]
    [InlineData("/srv/recordings/away")]
    public async Task ANameThatIsNotOneOfTheDriversOwnNeverBecomesAFileToRemove(string recordingId)
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        string away = Path.Combine(Path.GetDirectoryName(room)!, "away.ts");
        File.WriteAllBytes(away, new byte[188]);
        string held = Holding(room, "left-alone");

        using HttpResponseMessage response = await Erase(client, recordingId);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"The driver answered {(int)response.StatusCode} to '{recordingId}'."
        );
        Assert.True(File.Exists(away), $"'{recordingId}' reached a file outside the output root.");
        Assert.True(File.Exists(held));
    }

    [Fact]
    public async Task ANameThatBeginsWithADotIsRefusedBecauseARecordingNeverHidesItself()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        string hidden = Path.Combine(room, $"{StorageViews.WriteProbePrefix}some{RecordingFile.Extension}");
        File.WriteAllBytes(hidden, new byte[188]);

        using HttpResponseMessage response = await Erase(
            client,
            $"{StorageViews.WriteProbePrefix}some"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.Rejected,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(File.Exists(hidden));
    }

    [Fact]
    public async Task ARootTheStorageListingDoesNotCarryIsNeverARootToRemoveFrom()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string held = Holding(RoomOf(driver), "kept-1");

        using HttpResponseMessage response = await Erase(client, "kept-1", "archive");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.UnknownOutputRoot,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(File.Exists(held));
    }

    [Fact]
    public async Task ARequestThatNamesNoRootIsRefusedRatherThanSearchingEveryRoot()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string held = Holding(RoomOf(driver), "kept-1");

        using HttpResponseMessage response = await client.DeleteAsync(
            $"{DriverEndpoints.Recordings}/kept-1",
            Soon()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.UnknownOutputRoot,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(File.Exists(held));
    }

    [Fact]
    public void ARootThatCannotBeReadIsRefusedRatherThanReadAsAlreadyGone()
    {
        string root = DriverUnderTest.NewRoot();
        string notADirectory = Path.Combine(root, "archive");

        File.WriteAllBytes(notADirectory, [0x47]);

        try
        {
            FileErasure refused = EraserOver(notADirectory).Erase("kept-1", "primary");

            Assert.Equal(ErasureRefusal.RootOutOfReach, refused.Refusal);
            Assert.False(refused.FileRemoved);
            Assert.True(File.Exists(notADirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ARootThatHoldsNothingAtAllIsRefusedBecauseThatIsWhatALostMountLooksLike()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        Assert.Empty(Directory.GetFileSystemEntries(RoomOf(driver)));

        using HttpResponseMessage response = await Erase(client, "never-written");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.OutputUnavailable,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
    }

    [Fact]
    public void ARootWithNoDirectoryOnItIsRefusedRatherThanReadAsAlreadyGone()
    {
        string root = DriverUnderTest.NewRoot();

        try
        {
            FileErasure refused = EraserOver(Path.Combine(root, "not-mounted")).Erase("kept-1", "primary");

            Assert.Equal(ErasureRefusal.RootOutOfReach, refused.Refusal);
            Assert.False(refused.FileRemoved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AFileThatWillNotComeOffTheDiskIsReportedAndLeftWhereItIs()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        string room = RoomOf(driver);
        Holding(room, "left-alone");
        string stuck = Path.Combine(room, RecordingFile.Of("stuck-1"));
        Directory.CreateDirectory(stuck);

        using HttpResponseMessage response = await Erase(client, "stuck-1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.FileLeftBehind,
            (await DriverUnderTest.Read(response, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(Directory.Exists(stuck));
    }

    [Fact]
    public async Task ARecordingBeingWrittenKeepsItsFileUntilTheSessionIsOver()
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

        Assert.True(File.Exists(held));

        using HttpResponseMessage refused = await Erase(client, "still-going");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.RecordingInProgress,
            (await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.True(File.Exists(held));

        using HttpResponseMessage stopped = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("writing"))}?reason=the test is over",
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        using HttpResponseMessage erased = await Erase(client, "still-going");

        Assert.Equal(HttpStatusCode.OK, erased.StatusCode);
        Assert.False(File.Exists(held));
    }

    [Fact]
    public async Task ARecordingBeingThrownAwayIsNotOneANewSessionMayWriteTo()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();
        TunerSessionManager manager = driver.Service<TunerSessionManager>();

        using IDisposable? claim = manager.ClaimForErasure("being-thrown-away");

        Assert.NotNull(claim);

        using HttpResponseMessage refused = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording(
                    "late",
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    "primary",
                    "being-thrown-away"
                )
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            SessionRefusalTitles.RecordingAlreadyExists,
            (await DriverUnderTest.Read(refused, DriverJson.Context.DriverProblem))?.Title
        );
        Assert.Empty(Directory.GetFiles(RoomOf(driver)));
    }

    [Fact]
    public async Task OnlyOneCallerAtATimeHoldsARecordingForThrowingAway()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        TunerSessionManager manager = driver.Service<TunerSessionManager>();

        IDisposable? first = manager.ClaimForErasure("one-at-a-time");

        Assert.NotNull(first);
        Assert.Null(manager.ClaimForErasure("one-at-a-time"));
        Assert.NotNull(manager.ClaimForErasure("another"));

        first.Dispose();

        using IDisposable? again = manager.ClaimForErasure("one-at-a-time");

        Assert.NotNull(again);
    }

    [Fact]
    public async Task ADriverThatCanThrowARecordingAwaySaysSoInItsGreeting()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Health, Soon());

        DriverHello? hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.RecordingErasure));
    }

    [Theory]
    [InlineData("a.ts", true)]
    [InlineData("down/a.ts", false)]
    [InlineData("../a.ts", false)]
    [InlineData("down/../../a.ts", false)]
    public void AFileIsOnlyReachedWhereItSitsDirectlyInTheRoom(string name, bool reached)
    {
        string room = Path.Combine(Path.GetTempPath(), "carina-room");

        Assert.Equal(reached, RecordingEraser.LiesDirectlyUnder(room, Path.Combine(room, name)));
    }

    [Fact]
    public void TheRoomItselfIsNotAFileInIt()
    {
        string room = Path.Combine(Path.GetTempPath(), "carina-room");

        Assert.False(RecordingEraser.LiesDirectlyUnder(room, room));
        Assert.False(RecordingEraser.LiesDirectlyUnder(room, room + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheContainmentCheckIsATripWireAndNotTheGuarantee()
    {
        Assert.False(RecordingEraser.NamesARecordingFile("down/a"));
        Assert.False(RecordingEraser.NamesARecordingFile("../a"));
        Assert.False(RecordingEraser.NamesARecordingFile("a/../../b"));
        Assert.False(RecordingEraser.NamesARecordingFile(".hidden"));
        Assert.False(RecordingEraser.NamesARecordingFile(null));
        Assert.False(RecordingEraser.NamesARecordingFile(string.Empty));
        Assert.True(RecordingEraser.NamesARecordingFile("k-90210"));

        Assert.Throws<ArgumentException>(() => RecordingFile.Of("down/a"));
        Assert.Throws<ArgumentException>(() => RecordingFile.Of("../a"));
    }

    private static RecordingEraser EraserOver(string room)
    {
        var configuration = new DriverConfiguration(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", room)],
            6,
            new TunerSettings(TunerBackend.Fake),
            []
        );

        return new RecordingEraser(
            configuration,
            new TunerSessionManager(
                configuration,
                new ScriptedTunerDeviceFactory(),
                TimeProvider.System,
                NullLogger<TunerSessionManager>.Instance
            ),
            NullLogger<RecordingEraser>.Instance
        );
    }
}
