using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Collection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed record OrphanRecovered(int Found, int Readopted, int Resumed, int Marked, int LeftOpen)
{
    public static readonly OrphanRecovered Nothing = new(0, 0, 0, 0, 0);

    public bool SaysAnything => Resumed > 0 || Marked > 0 || LeftOpen > 0;
}

/// <summary>
/// Takes back the recordings the ledger still calls running when the driver greets this side as an
/// instance other than the one those recordings were left on — which is every start of this
/// application, and every restart of the driver under it. The rows it starts from are the ones with
/// no outcome, and what it holds them against is the session list that same greeting was followed
/// by. A connection that merely dropped never gets here.
///
/// It asks the driver to start a session again under the recording's own name and output root,
/// which is how a recording carries on into the file it already has. What it will never do is call
/// one of them complete: completing is what this side asks for, and every row this service touches
/// is one nobody asked to stop. A recording it gives up on keeps its file.
/// </summary>
public sealed class OrphanRecoveryService(
    IServiceScopeFactory scopes,
    IDriverClient driver,
    IRecordingFileWeigher weigher,
    TimeProvider clock,
    ILogger<OrphanRecoveryService> logger) : IDriverSessionResyncHook
{
    private string? takenUpUnder;

    public async Task ReadoptAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken)
        => Report(await RecoverAsync(hello, sessions, cancellationToken));

    public async Task<OrphanRecovered> RecoverAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(sessions);

        bool another = takenUpUnder is { } before
                       && !string.Equals(before, hello.InstanceId, StringComparison.Ordinal);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Recording> left = await InFlightAsync(cancellationToken);
        var tally = new Tally();

        foreach (Recording recording in left)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await OneAsync(recording, sessions, another, now, tally, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure)
            {
                logger.LogError(
                    failure,
                    "Recording {Recording} could not be taken back, which leaves it in flight for the next pass "
                    + "to try again and leaves its file where it is.",
                    recording.Id.Wire);
            }
        }

        takenUpUnder = hello.InstanceId;

        return tally.Read(left.Count);
    }

    private async Task OneAsync(
        Recording recording,
        IReadOnlyList<SessionSnapshot> sessions,
        bool another,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        SessionSnapshot? named = SessionOf(sessions, recording);

        if (SessionRefusalReading.FilledTheDisk(named))
        {
            await FailOnAFullDiskAsync(recording, now, tally, cancellationToken);

            return;
        }

        SessionSnapshot? standing = StillWriting(named);
        var sighting = new OrphanSighting(
            another,
            standing is not null,
            OrphanRecovery.StillOnAir(
                await GuideSaysAsync(recording, cancellationToken),
                WindowIsStillOpen(recording, now)));

        switch (OrphanRecovery.For(sighting))
        {
            case OrphanTreatment.ReadoptTheSession:
                await ReadoptOneAsync(recording, standing!, now, tally, cancellationToken);

                break;

            case OrphanTreatment.ResumeIntoTheSameFile:
                await ResumeAsync(recording, another, now, tally, cancellationToken);

                break;

            case OrphanTreatment.MarkWhatWasLeftBehind when ReachedTheEndItWasOpenedWith(named):
                LeaveForTheWatch(recording);

                break;

            default:
                await MarkAsync(recording, another, now, tally, cancellationToken);

                break;
        }
    }

    private async Task ReadoptOneAsync(
        Recording recording,
        SessionSnapshot standing,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        await ApplyAsync(
            recording.Id,
            loaded =>
            {
                bool named = loaded.TunerDeviceId is not null;

                RecordingResumption.Adopt(loaded, standing.DeviceId);

                return RecordingResumption.CloseAnyOpenBreak(loaded, now)
                       || (!named && loaded.TunerDeviceId is not null);
            },
            cancellationToken);

        tally.Readopted++;
    }

    private async Task ResumeAsync(
        Recording recording,
        bool another,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        RecordingFault fault = OrphanRecovery.WhyNothingWasWritingIt(another);

        await ApplyAsync(
            recording.Id,
            loaded => RecordingResumption.OpenABreak(loaded, fault, now),
            cancellationToken);

        if (await TuneOfAsync(recording, cancellationToken) is not { } tune)
        {
            tally.LeftOpen++;

            return;
        }

        DriverCall<SessionSnapshot> answer = await driver.StartSessionAsync(
            RecordingResumption.Request(recording, tune),
            cancellationToken);

        if (!answer.TryGetValue(out SessionSnapshot? reopened))
        {
            tally.LeftOpen++;
            WhyItWasNotOpened(recording, answer);

            return;
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;

        await ApplyAsync(
            recording.Id,
            loaded =>
            {
                RecordingResumption.Adopt(loaded, reopened.DeviceId);
                RecordingResumption.CloseAnyOpenBreak(loaded, at);

                return true;
            },
            cancellationToken);

        tally.Resumed++;

        logger.LogInformation(
            "Recording {Recording} was left running by a driver that is gone and carries on into the file it "
            + "already has.",
            recording.Id.Wire);
    }

    /// <summary>
    /// A driver that refuses because a session of this recording's name, or a writer on this
    /// recording, is already there is not a driver that turned the recording away: the pass that
    /// watches the stream opens the same session, and one that landed between the list this side
    /// was handed and this request reads exactly like a refusal. The break stays open either way
    /// and the next pass closes it on whatever is running, so that reading is said rather than the
    /// one that would have the recording stopped.
    /// </summary>
    private void WhyItWasNotOpened(Recording recording, DriverCall<SessionSnapshot> answer)
    {
        if (answer.Problem?.Title is SessionRefusalTitles.DuplicateSession
            or SessionRefusalTitles.RecordingAlreadyExists)
        {
            logger.LogInformation(
                "Recording {Recording} is still on the air and the driver already holds a stream of its own name "
                + "({Problem}), so a second one was not opened for it; what is running keeps the file and the "
                + "break is closed by the pass that watches it.",
                recording.Id.Wire,
                answer.Problem?.Title);

            return;
        }

        logger.LogWarning(
            "Recording {Recording} is still on the air and the driver would not take it up again "
            + "({Outcome}, {Problem}); it stays interrupted and its file is untouched.",
            recording.Id.Wire,
            answer.Outcome,
            answer.Problem?.Title);
    }

    /// <summary>
    /// Leaves a recording whose session the driver ended at the end it was opened with to the pass
    /// that watches the stream, which judges it against its file instead of marking it.
    /// </summary>
    private void LeaveForTheWatch(Recording recording)
        => logger.LogInformation(
            "Recording {Recording} was stopped by the driver at the end it was opened with, so it is left for "
            + "the pass that watches the stream to judge rather than marked for what was left of it.",
            recording.Id.Wire);

    private static bool ReachedTheEndItWasOpenedWith(SessionSnapshot? session)
        => session is { StopReason: SessionStopReason.EndTimeReached };

    private async Task MarkAsync(
        Recording recording,
        bool another,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        long? weighed = await weigher.WeighAsync(recording.OutputRoot, recording.FileName, cancellationToken);
        QualityBands bands = await BandsAsync(now, cancellationToken);
        RecordingOutcome outcome = OrphanRecovery.WhatIsLeftOf(weighed);

        bool marked = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                foreach (RecordingFault fault in OrphanRecovery.WhyItEndedWhereItDid(
                             another,
                             weighed,
                             RecordingQuality.Of(loaded.Counters, loaded.ScrambledPackets, bands).Scrambled))
                {
                    loaded.Note(new OutcomeDetail(fault, null, string.Empty, now));
                }

                loaded.Settle(outcome, weighed ?? 0, now);

                return true;
            },
            cancellationToken);

        if (!marked)
        {
            return;
        }

        tally.Marked++;

        logger.LogWarning(
            "Recording {Recording} was over and nothing was writing it, so it ends {Outcome} against a file of "
            + "{Bytes} byte(s) that stays where it is.",
            recording.Id.Wire,
            outcome,
            weighed);
    }

    /// <summary>
    /// A recording the driver stopped writing because the disk it was on had no room left is not
    /// put back on a stream: the disk is the same one, and a second attempt writes nothing. It ends
    /// here naming the full disk and keeps the file it has. This is the reading the pass that
    /// watches a running recording already makes, and recovery has to make it too — a driver that
    /// is replaced, or an application that is restarted, would otherwise walk past the reason and
    /// open the recording again on a disk that is still full.
    /// </summary>
    private async Task FailOnAFullDiskAsync(
        Recording recording,
        DateTime now,
        Tally tally,
        CancellationToken cancellationToken)
    {
        long? weighed = await weigher.WeighAsync(recording.OutputRoot, recording.FileName, cancellationToken);

        bool failed = await ApplyAsync(
            recording.Id,
            loaded =>
            {
                foreach (RecordingFault fault in RecordingFaults.OfAFullDisk(weighed))
                {
                    loaded.Note(new OutcomeDetail(fault, null, string.Empty, now));
                }

                loaded.Settle(RecordingOutcome.Failed, weighed ?? 0, now);

                return true;
            },
            cancellationToken);

        if (!failed)
        {
            return;
        }

        tally.Marked++;

        logger.LogWarning(
            "Recording {Recording} was left by a stream the driver ended for want of room on the disk, so it "
            + "fails here rather than being opened again onto a disk with no room; its file of {Bytes} byte(s) "
            + "stays where it is.",
            recording.Id.Wire,
            weighed);
    }

    private void Report(OrphanRecovered recovered)
    {
        if (!recovered.SaysAnything)
        {
            return;
        }

        logger.LogInformation(
            "Of {Found} recording(s) the ledger still called running, {Readopted} were still being written, "
            + "{Resumed} carried on into the file they already had, {Marked} were marked for what was left of "
            + "them, and {LeftOpen} could not be taken up and stay interrupted.",
            recovered.Found,
            recovered.Readopted,
            recovered.Resumed,
            recovered.Marked,
            recovered.LeftOpen);
    }

    private static bool WindowIsStillOpen(Recording recording, DateTime now)
        => recording.AbortedAt is null && recording.ExpectedWindowEnd > now;

    private static SessionSnapshot? SessionOf(IReadOnlyList<SessionSnapshot> sessions, Recording recording)
    {
        SessionId named = RecordingSessions.Named(recording.Id);

        foreach (SessionSnapshot session in sessions)
        {
            if (session.SessionId.Equals(named))
            {
                return session;
            }
        }

        return null;
    }

    private static SessionSnapshot? StillWriting(SessionSnapshot? session)
        => session is
        {
            Concluded: false,
            State: SessionState.Requested or SessionState.Active or SessionState.Stopping,
        }
            ? session
            : null;

    private async Task<QualityBands> BandsAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IReadOnlyList<QualityThreshold> held = await scope.ServiceProvider
            .GetRequiredService<IQualityThresholdRepository>()
            .ListAsync(cancellationToken);

        return QualityThresholdStanding.Bands(QualityThresholdStanding.Over(held, now));
    }

    private async Task<GuideStanding> GuideSaysAsync(Recording recording, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IAnnouncedProgrammes programmes = scope.ServiceProvider.GetRequiredService<IAnnouncedProgrammes>();
        DateTime? heardWholeAt = await programmes.HeardWholeAtAsync(
            recording.NetworkId.Value,
            recording.ServiceId.Value,
            cancellationToken);

        return GuideReading.Of(
            await programmes.FindAsync(recording.Programme.Id, cancellationToken),
            heardWholeAt);
    }

    private async Task<IReadOnlyList<Recording>> InFlightAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IRecordingRepository>()
            .ListInFlightAsync(cancellationToken);
    }

    private async Task<TuneParams?> TuneOfAsync(Recording recording, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        TuningResolution resolution = await scope.ServiceProvider
            .GetRequiredService<IServiceTuningDirectory>()
            .ResolveTuningAsync(recording.NetworkId, recording.ServiceId, cancellationToken);

        if (resolution.Tuning is not { } tuning)
        {
            logger.LogWarning(
                "Recording {Recording} is on a service this catalogue can no longer tune ({Refusal}), so there is "
                + "no stream to put it back on.",
                recording.Id.Wire,
                resolution.Refusal);

            return null;
        }

        return tuning.Typed();
    }

    /// <summary>
    /// One read, one change, one write. Something else writing the same row while this runs is the
    /// pass that watches the stream, and it is holding the same facts this is: the change is
    /// dropped rather than put on top, and the next pass finds the row as it is.
    /// </summary>
    private async Task<bool> ApplyAsync(
        RecordingId id,
        Func<Recording, bool> change,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IRecordingRepository recordings = scope.ServiceProvider.GetRequiredService<IRecordingRepository>();
        Recording? loaded = await recordings.FindAsync(id, cancellationToken);

        if (loaded is null || !loaded.IsInFlight || !change(loaded))
        {
            return false;
        }

        try
        {
            await recordings.SaveAsync(loaded, cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException collision)
        {
            logger.LogInformation(
                collision,
                "Recording {Recording} was written by something else while it was being taken back; what that "
                + "wrote stands and the next pass reads the row again.",
                id.Wire);

            return false;
        }
    }

    private sealed class Tally
    {
        public int Readopted { get; set; }

        public int Resumed { get; set; }

        public int Marked { get; set; }

        public int LeftOpen { get; set; }

        public OrphanRecovered Read(int found)
            => new(found, Readopted, Resumed, Marked, LeftOpen);
    }
}
