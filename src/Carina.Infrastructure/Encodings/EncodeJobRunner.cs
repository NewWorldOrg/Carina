using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Takes one job the ledger holds as running through to its end: finds the recording, chooses the
/// encoder against what this machine can do, has ffmpeg write the work file, and places the
/// artefact, or fails the job for one of the reasons the ledger holds. Whatever the end, what the
/// job still owes a removal for is swept; a stop asked for by the caller leaves the job running in
/// the ledger.
/// </summary>
/// <remarks>
/// The run writes on the job where it ran, each programme's id and start before its output is read,
/// and its headway at every tenth and at least every <see cref="HeartbeatEvery"/>. The chapters come
/// from the recording's detection by the version of the model in use, as <see cref="EncodeChapterChoice"/>
/// decides; a recording that version has not detected yet is encoded with no chapters, without waiting, and
/// they are written into the artefact once it has. Where they came from goes into the ledger before the
/// encode starts, whatever the answer; any chapters are handed to the encode as a metadata file recorded as
/// a scratch file. Before the artefact is placed, the text of the recording's captions is put into the work
/// file as a text track when it was taken by then. Once a job has completed, what earlier jobs of the
/// recording made is replaced; a failure there leaves the job completed and is settled at the next start.
/// </remarks>
public sealed class EncodeJobRunner(
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    IRecordingRepository recordings,
    EncodePlaces places,
    EncodeScratchFiles scratch,
    EncodeArtefactPlacer placer,
    EncodeArtefactSuccession succession,
    EncodeScratchCleaner cleaner,
    IMachineCapabilityReader machine,
    ISourceLengthReader lengths,
    ISourceHeadReader heads,
    EncodeChapterChoices choices,
    IEncodeChapterRepository chapters,
    ICaptionRecords captionRecords,
    CaptionTrackMux captionTracks,
    MachineSettings programmes,
    EncodeSettings settings,
    IEncodeAutoRunReader autoRun,
    TimeProvider clock,
    ILogger<EncodeJobRunner> logger)
{
    public const int Tenths = 10;

    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(10);

    private const int LongestComplaint = 500;

    public async Task<EncodeJobStatus> RunAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status is not EncodeJobStatus.Running)
        {
            throw new InvalidOperationException($"Only a job the ledger holds as running is run, and this one stands at {job.Status}.");
        }

        EncodeProfile profile = await profiles.FindAsync(job.ProfileId, cancellationToken)
            ?? throw new InvalidOperationException($"Job {job.Id.Wire} was queued with a profile the ledger no longer holds.");

        if (await recordings.FindAsync(job.RecordingId, cancellationToken) is not { } recording)
        {
            return await RefuseAsync(job, EncodeFailure.SourceMissing, "the ledger holds no recording for this job", cancellationToken);
        }

        if (places.WhereTheRecordingIs(recording.OutputRoot) is not { } mounted)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.CapabilityUnavailable,
                $"nothing tells this process where output root '{recording.OutputRoot.Value}' is mounted",
                cancellationToken);
        }

        var source = new FileInfo(Path.Combine(mounted, recording.FileName.Value));

        if (!source.Exists)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.SourceMissing,
                $"the recording file is not where the ledger says under output root '{recording.OutputRoot.Value}'",
                cancellationToken);
        }

        if (source.Length is 0)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.SourceMissing,
                $"the recording file under output root '{recording.OutputRoot.Value}' holds nothing",
                cancellationToken);
        }

        EncodePlan plan = EncodePlans.For(profile, settings.Prefer, await machine.ReadAsync(cancellationToken));

        if (plan.Encoder is not { } encoder)
        {
            return await RefuseAsync(job, plan.Refused ?? EncodeFailure.CapabilityUnavailable, plan.Note, cancellationToken);
        }

        job.Routed(EncodeRoute.Of(settings.Prefer, plan));

        if (plan.Swerved is { } swerve)
        {
            logger.LogWarning(
                "Job {Job} asked for the {Asked} and runs on the {Ran} instead ({Swerve}): {Note}",
                job.Id.Wire,
                settings.Prefer,
                encoder,
                swerve,
                plan.Note);
        }

        SourceLengthReading whole = await lengths.ReadAsync(source.FullName, cancellationToken);

        if (!whole.Measured)
        {
            logger.LogWarning(
                "Job {Job} runs without a whole to measure against ({Fault}): {Note}",
                job.Id.Wire,
                whole.Fault,
                whole.Note);
        }

        SourceHeadReading head = await heads.ReadAsync(source.FullName, recording.ServiceId, cancellationToken);

        if (head.HeadSkip is not { } headSkip)
        {
            return await RefuseAsync(job, WhatAnUnreadHeadIsCalled(head), WhyTheHeadWentUnread(head), cancellationToken);
        }

        if (!EncodeTimeline.WithinReach(headSkip))
        {
            return await RefuseAsync(
                job,
                EncodeFailure.HeadTooFar,
                $"the first picture that can be decoded lies {headSkip.TotalSeconds.ToString(FfmpegEncodeInvocation.Seconds, CultureInfo.InvariantCulture)} s into the source, further than the {EncodeTimeline.MostHeadSkip.TotalSeconds:0} s a run skips",
                cancellationToken);
        }

        var timeline = new EncodeTimeline(head.Start!.Value, headSkip, whole.Length, null);
        job.Aligned(timeline);

        int cores = Math.Min((await autoRun.ReadAsync(cancellationToken)).MostCores, programmes.Cores);

        EncodeChapterChoice choice = await choices.ReadAsync(recording.Id, cancellationToken);
        string? metadata = await ChosenAsync(job, choice, timeline, cancellationToken);
        string? breaks = null;

        if (metadata is not null)
        {
            if (await scratch.RecordAsync(job, EncodeScratchKind.Chapters, job.ChaptersFileName, cancellationToken) is not { } written)
            {
                return await RefuseAsync(
                    job,
                    EncodeFailure.CapabilityUnavailable,
                    $"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted",
                    cancellationToken);
            }

            await ChapterMetadataFile.WriteAsync(written, metadata, cancellationToken);
            breaks = written;
        }

        if (await scratch.RecordAsync(job, EncodeScratchKind.WorkFile, job.WorkFileName, cancellationToken) is not { } work)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.CapabilityUnavailable,
                $"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted",
                cancellationToken);
        }

        logger.LogInformation(
            "Job {Job} starts attempt {Attempt} on the {Encoder} over {Cores} core(s), {Whole} of source to get through after skipping {HeadSkip} s of head; the artefact's zero is {CaptionShift} s on the source's clock.",
            job.Id.Wire,
            job.Attempt,
            encoder,
            cores,
            timeline.Expected is { } expected ? expected.ToString("c", CultureInfo.InvariantCulture) : "an unmeasured length",
            headSkip.TotalSeconds.ToString(FfmpegEncodeInvocation.Seconds, CultureInfo.InvariantCulture),
            timeline.CaptionShift.TotalSeconds.ToString(FfmpegEncodeInvocation.Seconds, CultureInfo.InvariantCulture));

        EncodeSound sound = EncodeSound.Of(recording.SnapshotAudio, recording.SnapshotSounds);
        EncodeRate rate = EncodeRate.Of(profile);

        Task<EncodeRunOutcome> EncodeAsync(EncodeRate at, string into)
        {
            var heard = new Heartbeat();

            return FfmpegEncodeRun.RunAsync(
                programmes.Programme,
                [
                    .. FfmpegEncodeInvocation.Arguments(
                        recording.ServiceId,
                        profile,
                        encoder,
                        programmes,
                        source.FullName,
                        cores,
                        headSkip,
                        sound,
                        breaks,
                        at),
                    .. FfmpegEncodeInvocation.Delivery(into),
                ],
                timeline.Expected,
                settings.StalledAfter,
                spawned => SpawnedAsync(job, spawned, cancellationToken),
                progress => TellAsync(job, progress, heard, cancellationToken),
                clock,
                cancellationToken);
        }

        EncodeRunOutcome ran = await EncodeAsync(rate, work);

        if (ran.Fault is EncodeRunFault.ProgrammeMissing)
        {
            return await RefuseAsync(job, EncodeFailure.CapabilityUnavailable, ran.Complained, cancellationToken);
        }

        if (ran.Fault is EncodeRunFault.Stalled)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.TimedOut,
                $"no headway was made for {settings.StalledAfter}, so the programme was stopped where it stood: {ran.Complained}",
                cancellationToken);
        }

        if (ran.ExitCode is not 0)
        {
            return await RefuseAsync(
                job,
                FfmpegComplaint.Classified(ran.Complained),
                $"the programme exited {ran.ExitCode}: {ran.Complained}",
                cancellationToken);
        }

        string kept = await NoLargerThanTheSourceAsync(job, new EncodeRemakeRun(source.FullName, work, rate, encoder, EncodeAsync), cancellationToken);

        await MeasureAsync(job, kept, cancellationToken);
        await CaptionAsync(job, kept, cancellationToken);

        EncodePlacementOutcome placed = await placer.PlaceAsync(job, cancellationToken);

        logger.LogInformation("Job {Job} ends {Status}; its artefact was {Placed}.", job.Id.Wire, job.Status, placed);

        EncodeJobStatus ended = await SweptAsync(job, cancellationToken);

        if (ended is EncodeJobStatus.Completed)
        {
            await SucceedAsync(job, cancellationToken);
        }

        return ended;
    }

    private async Task SucceedAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        try
        {
            await succession.SettleAsync(job.RecordingId, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogError(
                failure,
                "Job {Job} completed, and what earlier jobs of recording {Recording} made could not be replaced; it is settled at the next start.",
                job.Id.Wire,
                job.RecordingId.Wire);
        }
    }

    /// <summary>
    /// Writes down that the chapters come from the recording's detection by the version in use, or from nothing,
    /// and hands back the stretches embedded in the recording's own time, or none.
    /// </summary>
    private async Task<string?> ChosenAsync(
        EncodeJob job,
        EncodeChapterChoice choice,
        EncodeTimeline timeline,
        CancellationToken cancellationToken)
    {
        PlaybackFileClock onTheArtefact = PlaybackFileClock.Of(timeline);
        IReadOnlyList<SegmentSpan> embedded = choice.Embedded(onTheArtefact);

        job.Judged(choice.Reading(clock.GetUtcNow().UtcDateTime));
        await jobs.SaveAsync(job, cancellationToken);
        await chapters.RecordAsync(job.Id, EncodeChapter.Mark(job.Id, choice.Chapters(onTheArtefact)), cancellationToken);

        if (choice.Standing.Detection is { } detection)
        {
            logger.LogInformation(
                "Job {Job} takes its chapters from the detection of its recording by version {Version}, which came to {State}: {Chapters} chapter(s) embedded.",
                job.Id.Wire,
                detection.ModelVersion,
                detection.State,
                embedded.Count);
        }
        else
        {
            logger.LogInformation(
                "Job {Job} is encoded with no chapters, because version {Version} has not detected its recording; they are written into the artefact once it has.",
                job.Id.Wire,
                choice.ModelVersion);
        }

        return embedded.Count is 0 ? null : ChapterMetadataFile.InRecordingTime(embedded);
    }

    /// <summary>
    /// Makes the work file a second time, coarser, when it came out larger than the recording, writes down which
    /// of the two the job keeps, and hands back where that one is. A recording whose size is not known is outgrown
    /// by nothing, a profile already at the coarsest is not made again, and a second run that fails leaves the
    /// first. The one not kept is swept with the rest of the job's scratch once it ends.
    /// </summary>
    private async Task<string> NoLargerThanTheSourceAsync(EncodeJob job, EncodeRemakeRun run, CancellationToken cancellationToken)
    {
        long recorded = BytesOf(run.Source);
        long first = BytesOf(run.Work);

        if (!EncodeRemakes.Outgrew(first, recorded))
        {
            return run.Work;
        }

        EncodeRate coarser = run.Rate.Coarser();

        if (coarser.On(run.Encoder) == run.Rate.On(run.Encoder))
        {
            logger.LogWarning(
                "Job {Job} made {Made} bytes, larger than its recording of {Recorded} bytes, at {Rate} on the {Encoder}, the coarsest there is, so it is not made again.",
                job.Id.Wire,
                first,
                recorded,
                run.Rate.On(run.Encoder),
                run.Encoder);

            return run.Work;
        }

        if (await scratch.RecordAsync(job, EncodeScratchKind.WorkFile, job.RemadeFileName, cancellationToken) is not { } again)
        {
            logger.LogWarning(
                "Job {Job} made {Made} bytes, larger than its recording of {Recorded} bytes, and nothing tells this process where output root '{Root}' is mounted to make it again.",
                job.Id.Wire,
                first,
                recorded,
                job.OutputRoot.Value);

            return run.Work;
        }

        job.Remaking();
        await jobs.SaveAsync(job, cancellationToken);

        logger.LogWarning(
            "Job {Job} made {Made} bytes, larger than its recording of {Recorded} bytes, and makes it again at {Coarser} on the {Encoder} in place of {Rate}.",
            job.Id.Wire,
            first,
            recorded,
            coarser.On(run.Encoder),
            run.Encoder,
            run.Rate.On(run.Encoder));

        EncodeRunOutcome ran = await run.EncodeAsync(coarser, again);
        long? second = ran.Succeeded && File.Exists(again) ? BytesOf(again) : null;

        job.MadeAgain(EncodeRemakes.Judge(recorded, first, second));
        await jobs.SaveAsync(job, cancellationToken);

        if (second is not { } made)
        {
            logger.LogWarning(
                "Job {Job} could not make it again ({Why}), and keeps the first of {Made} bytes against a recording of {Recorded} bytes.",
                job.Id.Wire,
                WhyARunMadeNothing(ran),
                first,
                recorded);
        }
        else
        {
            logger.Log(
                job.Remade is EncodeRemake.LargerThanTheSource ? LogLevel.Information : LogLevel.Warning,
                "Job {Job} made it again to {Second} bytes against a recording of {Recorded} bytes, and comes to {Remade}.",
                job.Id.Wire,
                made,
                recorded,
                job.Remade);
        }

        return job.KeptWorkFileName.Equals(job.RemadeFileName) ? again : run.Work;
    }

    private static string WhyARunMadeNothing(EncodeRunOutcome ran)
        => ran.Fault is { } fault
            ? $"{fault}: {ran.Complained}"
            : $"the programme exited {ran.ExitCode?.ToString(CultureInfo.InvariantCulture)}: {ran.Complained}";

    private static long BytesOf(string path)
    {
        var file = new FileInfo(path);

        return file.Exists ? file.Length : 0;
    }

    private static EncodeFailure WhatAnUnreadHeadIsCalled(SourceHeadReading head)
        => head.Fault switch
        {
            SourceHeadFault.SaidNothing => EncodeFailure.HeadTooFar,
            SourceHeadFault.Refused => EncodeFailure.FfmpegExitedNonZero,
            _ => EncodeFailure.CapabilityUnavailable,
        };

    private static string WhyTheHeadWentUnread(SourceHeadReading head)
        => head.Fault is SourceHeadFault.Refused
            ? $"the programme exited {head.ExitCode} while reading the head of the source: {head.Note}"
            : $"the head of the source could not be read ({head.Fault}), so nothing says where the artefact's clock would begin: {head.Note}";

    private async Task MeasureAsync(EncodeJob job, string work, CancellationToken cancellationToken)
    {
        SourceLengthReading made = await lengths.ReadAsync(work, cancellationToken);

        if (made.Length is not { } length)
        {
            logger.LogWarning(
                "Job {Job} wrote an artefact whose length could not be measured ({Fault}), so its clock goes unchecked against the source's: {Note}",
                job.Id.Wire,
                made.Fault,
                made.Note);

            return;
        }

        job.Measured(length);
        EncodeTimeline timeline = job.Timeline!;

        if (timeline.LengthsAgree is false)
        {
            logger.LogWarning(
                "Job {Job} wrote an artefact of {Made} where the source had {Expected} left after the head skip: {Drift} s apart, more than the {Tolerance} s the two clocks are allowed. The job completes; a caption near the end may land off its picture.",
                job.Id.Wire,
                length.ToString("c", CultureInfo.InvariantCulture),
                timeline.Expected!.Value.ToString("c", CultureInfo.InvariantCulture),
                timeline.Drift!.Value.TotalSeconds.ToString(FfmpegEncodeInvocation.Seconds, CultureInfo.InvariantCulture),
                EncodeTimeline.Tolerance.TotalSeconds.ToString(FfmpegEncodeInvocation.Seconds, CultureInfo.InvariantCulture));

            return;
        }

        logger.LogInformation(
            "Job {Job} wrote an artefact of {Made}; the source had {Expected} left after the head skip ({Drift} s apart).",
            job.Id.Wire,
            length.ToString("c", CultureInfo.InvariantCulture),
            timeline.Expected is { } expected ? expected.ToString("c", CultureInfo.InvariantCulture) : "an unmeasured length",
            timeline.Drift is { } drift ? drift.TotalSeconds.ToString(FfmpegEncodeInvocation.Seconds, CultureInfo.InvariantCulture) : "an unknown distance");
    }

    /// <summary>
    /// Writes a copy of the work file with the text track of the recording's captions put in, for the placer to
    /// place in its stead, when the captions were taken with their text by now. Whatever stops that leaves the
    /// work file as the encode wrote it to be placed, and the job goes on to complete.
    /// </summary>
    private async Task CaptionAsync(EncodeJob job, string work, CancellationToken cancellationToken)
    {
        if (await recordings.FindAsync(job.RecordingId, cancellationToken) is not { CaptionState: CaptionState.Ready, CaptionsMadeAt: { } madeAt }
            || await captionRecords.ReadAsync(job.RecordingId, cancellationToken) is not { Lines: not null } record)
        {
            logger.LogInformation(
                "Job {Job} places its artefact with no text track of captions, because the recording's captions with their text are not taken yet; the track is put in once they are.",
                job.Id.Wire);

            return;
        }

        CaptionTrackMade made = await CaptionedAsync(job, work, record, cancellationToken);

        job.Tracked(made.Outcome, madeAt);
        await jobs.SaveAsync(job, cancellationToken);

        logger.Log(
            made.Outcome is EncodeCaptionTrack.Failed ? LogLevel.Warning : LogLevel.Information,
            "Job {Job} came to {Outcome} for the text track of captions in its artefact. {Note}",
            job.Id.Wire,
            made.Outcome,
            made.Note);
    }

    private async Task<CaptionTrackMade> CaptionedAsync(EncodeJob job, string work, CaptionRecord record, CancellationToken cancellationToken)
    {
        try
        {
            return await captionTracks.MakeAsync(job, work, record, CaptionTrackNames.Making(job), cancellationToken);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            return CaptionTrackMade.Failed($"putting it in ended in {failure.GetType().Name}: {ProgrammeNote.Of(failure.Message, LongestComplaint)}");
        }
    }

    private async Task SpawnedAsync(EncodeJob job, RunningProgramme spawned, CancellationToken cancellationToken)
    {
        job.Spawned(spawned);
        await jobs.SaveAsync(job, cancellationToken);

        logger.LogInformation("Job {Job} runs as process {Process}, begun {Began:O}.", job.Id.Wire, spawned.ProcessId, spawned.StartedAt);
    }

    private async Task TellAsync(EncodeJob job, EncodeProgress progress, Heartbeat heard, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        job.Reached(progress, now);

        int tenths = progress.Portion is { } portion ? (int)Math.Floor(portion * Tenths) : -1;
        bool anotherTenth = tenths != heard.TenthsTold && progress.Portion is not null;
        bool dueAnyway = heard.SavedAt is not { } saved || now - saved >= HeartbeatEvery;

        if (!anotherTenth && !dueAnyway && !progress.Ended)
        {
            return;
        }

        heard.SavedAt = now;
        await jobs.SaveAsync(job, cancellationToken);

        if (progress.Portion is not { } done || (!anotherTenth && !progress.Ended))
        {
            return;
        }

        heard.TenthsTold = tenths;
        logger.LogInformation(
            "Job {Job} is {Percent}% of the way through at {Speed}x, {Left} left.",
            job.Id.Wire,
            (int)Math.Round(done * 100),
            progress.Speed.ToString("0.00", CultureInfo.InvariantCulture),
            progress.Left is { } left ? left.ToString("c", CultureInfo.InvariantCulture) : "an unknown time");
    }

    private async Task<EncodeJobStatus> RefuseAsync(
        EncodeJob job,
        EncodeFailure failure,
        string note,
        CancellationToken cancellationToken)
    {
        job.Fail(failure, note, clock.GetUtcNow().UtcDateTime);
        await jobs.SaveAsync(job, cancellationToken);

        logger.LogWarning("Job {Job} fails as {Failure}: {Note}", job.Id.Wire, failure, job.Failure!.Note);

        return await SweptAsync(job, cancellationToken);
    }

    private async Task<EncodeJobStatus> SweptAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        if (job.HasEnded)
        {
            await cleaner.ClearAsync(job, cancellationToken);
        }

        return job.Status;
    }

    private sealed record EncodeRemakeRun(
        string Source,
        string Work,
        EncodeRate Rate,
        EncodeEncoder Encoder,
        Func<EncodeRate, string, Task<EncodeRunOutcome>> EncodeAsync);

    private sealed class Heartbeat
    {
        public int TenthsTold { get; set; } = -1;

        public DateTime? SavedAt { get; set; }
    }
}
