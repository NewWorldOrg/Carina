using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

/// <summary>
/// A recording that lost the session it was being written on is put back on a stream under its own
/// name, and what makes that carry on rather than start over is that the writer opens the file it
/// already has and writes on the end of it. Nothing in the driver's own vocabulary says "resume",
/// so this is what holds it.
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
