using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class PromisedWindowOutcomeTests
{
    private static readonly DateTime Airs = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = Airs.AddMinutes(200);

    private static readonly Margin Ahead = Margin.OfSeconds(120);

    private static readonly Margin Behind = Margin.OfSeconds(180);

    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan OneRoundOfTheTick = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan NoTunerForFiftyMinutes = TimeSpan.FromMinutes(50);

    [Fact]
    public void AskedForAheadOfTimeAndStartedOnTheDotItCoversAllOfItsWindow()
    {
        RecordingWindow window = Promised(Airs.AddHours(-1));

        Assert.Equal(Airs.AddSeconds(-90), window.Start);
        Assert.Equal(Ends.AddSeconds(180), window.End);

        RecordingVerdict verdict = Judge(window, window.Length);

        Assert.Equal(1.0, verdict.Coverage);
        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void AskedForWhileTheBroadcastIsAlreadyRunningItIsPromisedFromThatMomentAndCoversAlmostAllOfIt()
    {
        DateTime asking = Airs.AddMinutes(50);
        RecordingWindow window = Promised(asking);

        Assert.Equal(asking + Lead, window.Start);
        Assert.Equal(Ends.AddSeconds(180), window.End);

        RecordingVerdict verdict = Judge(window, window.Length - OneRoundOfTheTick);

        Assert.InRange(verdict.Coverage, 0.995, 1.0);
        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void AskedForTooLateForItsOwnMarginItIsPromisedFromTheAskingAndCoversAlmostAllOfIt()
    {
        DateTime asking = Airs.AddSeconds(-60);
        RecordingWindow window = Promised(asking);

        Assert.Equal(asking + Lead, window.Start);
        Assert.Equal(Ends.AddSeconds(180), window.End);

        RecordingVerdict verdict = Judge(window, window.Length - OneRoundOfTheTick);

        Assert.InRange(verdict.Coverage, 0.995, 1.0);
        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void AskedForAheadOfTimeButStartedFiftyMinutesLateItIsShortOfItsWindowAndSaysSo()
    {
        RecordingWindow window = Promised(Airs.AddHours(-1));

        RecordingVerdict verdict = Judge(window, window.Length - NoTunerForFiftyMinutes);

        Assert.InRange(verdict.Coverage, 0.74, 0.76);
        Assert.Contains(RecordingFault.ShortOfTheWindow, verdict.Faults);
        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

    [Fact]
    public void TheLatenessIsStillSeenWhenTheBroadcastWasAlreadyRunningWhenItWasAskedFor()
    {
        DateTime asking = Airs.AddMinutes(50);
        RecordingWindow window = Promised(asking);

        RecordingVerdict verdict = Judge(window, window.Length - NoTunerForFiftyMinutes);

        Assert.InRange(verdict.Coverage, 0.66, 0.68);
        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

    private static RecordingWindow Promised(DateTime askedAt)
    {
        Reservation asked = Reservation.Plan(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Airs),
            null,
            Priority.Default,
            Airs,
            Ends,
            true,
            Ahead,
            Behind,
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [new ProgrammeGenre(7, 1)],
                Airs.AddHours(-6)),
            null,
            BroadcastGroupRole.Standalone,
            askedAt);

        return RecordingWindow.Promised(asked.EffectiveStartAt, asked.EffectiveEndAt, Lead);
    }

    private static RecordingVerdict Judge(RecordingWindow window, TimeSpan written)
        => CompletionEvaluator.Judge(
            new RecordingEvidence(Weighing(written), written, window.Start, window.End, window.End),
            ExpectedBitrate.Terrestrial,
            CompletionTolerance.Default);

    private static long Weighing(TimeSpan written) => (long)(15_400_000 * written.TotalSeconds / 8.0);
}
