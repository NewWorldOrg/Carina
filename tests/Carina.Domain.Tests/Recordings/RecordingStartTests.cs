using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingStartTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Opens = Now.AddMinutes(1);

    private static readonly DateTime Closes = Now.AddMinutes(31);

    [Fact(DisplayName = "BR-KD-024: a start tried before the reservation's window opens is still ahead of it, and one tried on the instant it opens is not")]
    public void AStartBeforeTheWindowOpensIsStillAhead()
    {
        RecordingTick due = Tick(TimeSpan.Zero);

        Assert.True(due.IsStillAhead(Opens.AddTicks(-1)));
        Assert.False(due.IsStillAhead(Opens));
        Assert.False(due.IsStillAhead(Closes));
    }

    [Fact(DisplayName = "BR-KS-003: the programme of a reservation is still on until its window, less the margin after it, closes")]
    public void TheProgrammeIsStillOnUntilTheWindowLessItsMarginCloses()
    {
        RecordingTick due = Tick(TimeSpan.FromMinutes(2));
        DateTime ends = Closes.AddMinutes(-2);

        Assert.True(due.ProgrammeStillOn(ends.AddTicks(-1)));
        Assert.False(due.ProgrammeStillOn(ends));
        Assert.False(due.ProgrammeStillOn(Closes.AddTicks(-1)));
    }

    [Fact(DisplayName = "BR-KS-003: with no margin after it, the programme is on until the window closes")]
    public void WithNoMarginTheProgrammeIsOnUntilTheWindowCloses()
    {
        RecordingTick due = Tick(TimeSpan.Zero);

        Assert.True(due.ProgrammeStillOn(Closes.AddTicks(-1)));
        Assert.False(due.ProgrammeStillOn(Closes));
    }

    [Fact(DisplayName = "a recording keeps the sound the guide announces when it begins, and what the reservation kept where the guide is still silent")]
    public void ARecordingKeepsTheSoundTheGuideAnnounces()
    {
        ProgrammeSnapshot kept = RecordingFactory.Snapshot(AudioMode.Stereo, 1);

        ProgrammeSnapshot both = kept.HeardFrom(Announced(AudioMode.DualMono, 2));
        ProgrammeSnapshot silent = kept.HeardFrom(Announced(AudioMode.Undetermined, ProgrammeSnapshot.SoundsUnannounced));
        ProgrammeSnapshot audioOnly = kept.HeardFrom(Announced(AudioMode.Surround, ProgrammeSnapshot.SoundsUnannounced));

        Assert.Equal((AudioMode.DualMono, 2), (both.Audio, both.Sounds));
        Assert.Same(kept, silent);
        Assert.Equal((AudioMode.Surround, 1), (audioOnly.Audio, audioOnly.Sounds));
        Assert.Equal(
            (kept.Name, kept.Summary, kept.Extended, kept.Genres, kept.CapturedAt),
            (both.Name, both.Summary, both.Extended, both.Genres, both.CapturedAt));
    }

    [Fact(DisplayName = "a guide that announces the sound the reservation already kept leaves the snapshot as it was")]
    public void AGuideThatSaysTheSameLeavesTheSnapshotAsItWas()
    {
        ProgrammeSnapshot kept = RecordingFactory.Snapshot(AudioMode.Stereo, 1);

        Assert.Same(kept, kept.HeardFrom(Announced(AudioMode.Stereo, 1)));
    }

    [Fact(DisplayName = "BR-KS-003: an attempt that began a recording counts as started")]
    public void AnAttemptThatBeganARecordingCountsAsStarted()
        => Assert.Same(RetryAttempt.Started, RetryAttempt.Of(true, true, Named()));

    [Fact(DisplayName = "BR-KS-003: an attempt the driver refused carries the class it named, or none")]
    public void AnAttemptTheDriverRefusedCarriesTheClassItNamed()
    {
        RetryAttempt named = RetryAttempt.Of(false, true, Named());
        RetryAttempt unnamed = RetryAttempt.Of(false, true, null);

        Assert.Equal(RetryResult.RefusedAgain, named.Result);
        Assert.Equal(TuneFailureKind.NoLock, named.TuneFailure);
        Assert.Equal([RecordingFault.TuneFailed], named.Faults);
        Assert.Equal(RetryResult.RefusedAgain, unnamed.Result);
        Assert.Empty(unnamed.Faults);
    }

    [Fact(DisplayName = "BR-KS-003: an attempt nothing answered, or one given up on this side, is unanswered")]
    public void AnAttemptNothingAnsweredIsUnanswered()
        => Assert.Same(RetryAttempt.Unanswered, RetryAttempt.Of(false, false, Named()));

    private static RecordingStartFailure Named()
        => RecordingStartFailure.TheTunerWouldNotTune(TuneFailureKind.NoLock);

    private static RecordingTick Tick(TimeSpan marginAfter)
    {
        ProgrammeRef programme = RecordingFactory.Programme();

        return new RecordingTick(
            ReservationId.New(),
            programme.NetworkId,
            programme.ServiceId,
            programme.EventId,
            programme.StartsAt,
            RecordingFactory.Snapshot(),
            Priority.Default,
            null,
            BroadcastGroupRole.Standalone,
            Opens,
            Closes,
            true,
            marginAfter,
            null);
    }

    private static Programme Announced(AudioMode audio, int sounds)
    {
        ProgrammeRef programme = RecordingFactory.Programme();

        return Programme.Rehydrate(
            programme.Id,
            new TransportStreamId(32736),
            programme.StartsAt,
            Now.AddMinutes(30),
            "A programme",
            string.Empty,
            false,
            Now,
            audio: audio,
            sounds: sounds);
    }
}
