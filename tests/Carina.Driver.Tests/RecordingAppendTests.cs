using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

/// <summary>
/// A recording put back on a stream under its own name has the writer open the file it already has
/// and write on the end of it.
/// </summary>
public sealed class RecordingAppendTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private static readonly byte[] AlreadyThere = [0x47, 0x1f, 0xff, 0x10];

    private readonly string root = Directory.CreateTempSubdirectory("carina-append-").FullName;
    private readonly ManualTimeProvider clock = new(Start);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverConfiguration Configuration =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", root)],
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
        );

    [Fact]
    public void AWriterOpenedOnARecordingWritesOnTheEndOfWhatIsAlreadyInTheFile()
    {
        string path = Path.Combine(root, RecordingFile.Of("k-carried"));

        using (var first = new RecordingWriter(root, "k-carried"))
        {
            first.Write(AlreadyThere);
        }

        using (var again = new RecordingWriter(root, "k-carried"))
        {
            again.Write([0x47, 0x00, 0x64, 0x11]);

            Assert.Equal(4, again.BytesWritten);
        }

        Assert.Equal([0x47, 0x1f, 0xff, 0x10, 0x47, 0x00, 0x64, 0x11], File.ReadAllBytes(path));
    }

    [Fact]
    public void ASessionTakenUpOnARecordingLeavesWhatWasAlreadyWrittenWhereItIs()
    {
        string path = Path.Combine(root, RecordingFile.Of("k-resumed"));

        File.WriteAllBytes(path, AlreadyThere);

        var manager = new TunerSessionManager(
            Configuration,
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        SessionStart taken = manager.Begin(new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-resumed"),
            Purpose = SessionPurpose.Recording,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
            OutputRoot = "primary",
            RecordingId = "k-resumed",
            EndsAt = Start.AddHours(1),
        });

        Assert.True(taken.TryGetSession(out TunerSession? session), taken.Detail);

        UntilItHasWritten(session, AlreadyThere.Length);

        session.Stop();
        session.WaitForEnd(Deadlock);
        session.Dispose();

        byte[] held = File.ReadAllBytes(path);

        Assert.True(
            held.Length > AlreadyThere.Length,
            "The session that was taken up on this recording wrote nothing onto the end of its file.");
        Assert.Equal(AlreadyThere, held[..AlreadyThere.Length]);
    }

    [Fact]
    public void AWriterOpenedOnAFileThatAlreadyHoldsSomethingSaysWhenItWasLastWritten()
    {
        string path = Path.Combine(root, RecordingFile.Of("k-held"));
        DateTime lastWritten = new(2026, 9, 28, 17, 54, 16, DateTimeKind.Utc);

        File.WriteAllBytes(path, AlreadyThere);
        File.SetLastWriteTimeUtc(path, lastWritten);

        using RecordingWriter again = new(root, "k-held");

        Assert.Equal(new DateTimeOffset(lastWritten, TimeSpan.Zero), again.AppendedAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWriterThatBeginsAFileSaysItCarriedOnFromNothing(bool anEmptyFileIsThere)
    {
        if (anEmptyFileIsThere)
        {
            File.WriteAllBytes(Path.Combine(root, RecordingFile.Of("k-new")), []);
        }

        using RecordingWriter first = new(root, "k-new");

        Assert.Null(first.AppendedAfter);
    }

    [Fact]
    public void ASessionTakenUpOnARecordingSaysWhenTheFileWasLastWrittenAndWhenItFirstWroteToIt()
    {
        string path = Path.Combine(root, RecordingFile.Of("k-seam"));
        DateTime lastWritten = new(2026, 9, 28, 17, 54, 16, DateTimeKind.Utc);

        File.WriteAllBytes(path, AlreadyThere);
        File.SetLastWriteTimeUtc(path, lastWritten);

        TunerSession session = Begun("k-seam");

        UntilItHasWritten(session, AlreadyThere.Length);

        Assert.Equal(new DateTimeOffset(lastWritten, TimeSpan.Zero), session.AppendedAfter);
        Assert.Equal(Start, session.FirstWrittenAt);

        session.Stop();
        session.WaitForEnd(Deadlock);
        session.Dispose();
    }

    [Fact]
    public void ASessionThatBeginsARecordingSaysItCarriedOnFromNothing()
    {
        TunerSession session = Begun("k-fresh");

        UntilItHasWritten(session, 0);

        Assert.Null(session.AppendedAfter);
        Assert.Equal(Start, session.FirstWrittenAt);

        session.Stop();
        session.WaitForEnd(Deadlock);
        session.Dispose();
    }

    private TunerSession Begun(string recordingId)
    {
        TunerSessionManager manager = new(
            Configuration,
            new ScriptedTunerDeviceFactory(),
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

        SessionStart taken = manager.Begin(new StartSessionRequest
        {
            SessionId = SessionId.Parse("s-" + recordingId),
            Purpose = SessionPurpose.Recording,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 55, 50001),
            OutputRoot = "primary",
            RecordingId = recordingId,
            EndsAt = Start.AddHours(1),
        });

        Assert.True(taken.TryGetSession(out TunerSession? session), taken.Detail);

        return session;
    }

    private static void UntilItHasWritten(TunerSession session, long bytes)
    {
        DateTime giveUp = DateTime.UtcNow + Deadlock;

        while (session.BytesRecorded <= bytes)
        {
            Assert.True(
                DateTime.UtcNow < giveUp,
                $"The session wrote {session.BytesRecorded} byte(s) and was waited on for {Deadlock}.");

            Thread.Sleep(10);
        }
    }
}
