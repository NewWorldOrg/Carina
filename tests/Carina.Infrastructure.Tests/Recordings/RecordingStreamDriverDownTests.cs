using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingStreamDriverDownTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime LastWrite = Airs.AddMinutes(10).AddSeconds(0.08);

    private static readonly DateTime CameBack = Airs.AddMinutes(11).AddSeconds(51.79);

    private static readonly DateTime FirstWriteAfter = Airs.AddMinutes(11).AddSeconds(52.6);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADriverThatStaysDownForMinutesLeavesAnInterruptionAsLongAsTheStretchTheFileMissed(
        bool theFileSaysWhenItWasLastWritten)
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new()
        {
            WhenAsked = DriverCall<SessionSnapshot>.Unreachable("the socket is not there"),
        };
        WeighedFiles files = new()
        {
            Weighs = 1_200_000_000,
            LastWritten = theFileSaysWhenItWasLastWritten ? LastWrite : null,
        };
        WatchClock clock = new(Airs.AddMinutes(10).AddSeconds(10));
        RecordingStreamSupervisor supervisor = Supervisor(ledger, driver, clock, files);

        for (int pass = 0; clock.Now < CameBack; pass++)
        {
            await supervisor.WatchAsync(Cancel);
            clock.Now = Airs.AddMinutes(10).AddSeconds(10 * (pass + 2));
        }

        Assert.Empty(ledger.Read(recording.Id).Interruptions);

        clock.Now = CameBack;
        driver.WhenAsked = DriverCall<SessionSnapshot>.Refused(new DriverProblem("noSuchSession", []));
        driver.WhenStarted = Live(recording, CameBack.AddSeconds(0.5));
        await supervisor.WatchAsync(Cancel);

        Interruption noticed = Assert.Single(ledger.Read(recording.Id).Interruptions);
        Assert.Equal(RecordingFault.DriverLost, noticed.Fault);
        Assert.Equal(theFileSaysWhenItWasLastWritten ? LastWrite : CameBack, noticed.OccurredAt);

        clock.Now = CameBack.AddSeconds(10);
        driver.Holding[RecordingSessions.Named(recording.Id)] = DriverCall<SessionSnapshot>.Reached(
            Live(recording, CameBack.AddSeconds(0.5)).Value! with
            {
                AppendedAfter = new DateTimeOffset(LastWrite),
                FirstWrittenAt = new DateTimeOffset(FirstWriteAfter),
            });
        await supervisor.WatchAsync(Cancel);

        Recording read = ledger.Read(recording.Id);
        Interruption placed = Assert.Single(read.Interruptions);
        RecordingGap gap = Assert.Single(read.Gaps);

        Assert.Equal(LastWrite, placed.OccurredAt);
        Assert.Equal(FirstWriteAfter, placed.ResumedAt);
        Assert.Equal(gap.From, placed.OccurredAt);
        Assert.Equal(gap.Until, placed.ResumedAt);
        Assert.Equal(112_520, read.MissedMs);
        Assert.Equal(1, read.ResumeCount);
    }

    [Fact]
    public async Task AFileLastWrittenBeforeTheStretchThatResumedLastOpensTheBreakWhereThatStretchResumed()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        recording.Interrupt(RecordingFault.DriverLost, Airs.AddMinutes(3));
        recording.Resume(Airs.AddMinutes(4));
        ledger.Hold(recording);
        WatchedDriver driver = new();
        WeighedFiles files = new() { Weighs = 1_200_000_000, LastWritten = Airs.AddMinutes(2) };

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)), files).WatchAsync(Cancel);

        Interruption opened = ledger.Read(recording.Id).Interruptions[^1];

        Assert.Equal(Airs.AddMinutes(4), opened.OccurredAt);
    }

    [Fact]
    public async Task AFileTheClockSaysWasWrittenAfterTheBreakWasNoticedOpensTheBreakWhenItWasNoticed()
    {
        Recording recording = InFlight();
        StreamLedger ledger = new();
        ledger.Hold(recording);
        WatchedDriver driver = new();
        WeighedFiles files = new() { Weighs = 1_200_000_000, LastWritten = Airs.AddMinutes(11) };

        await Supervisor(ledger, driver, new WatchClock(Airs.AddMinutes(10)), files).WatchAsync(Cancel);

        Assert.Equal(Airs.AddMinutes(10), Assert.Single(ledger.Read(recording.Id).Interruptions).OccurredAt);
    }
}
