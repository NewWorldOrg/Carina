using System.Globalization;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Takes one job the ledger holds as running through to its end: the recording is found and looked
/// at, the encoder is chosen against what this machine can do, ffmpeg writes the work file the
/// ledger was told about, and the artefact is placed by the ledger or the job fails for one of the
/// six reasons the ledger holds. Whatever the end, what the job still owes a removal for is swept.
/// A stop asked for by the caller is the one thing that leaves the job as it was: it stays running
/// in the ledger for the next start to put back (BR-ED2-011).
/// </summary>
public sealed class EncodeJobRunner(
    IEncodeJobRepository jobs,
    IEncodeProfileRepository profiles,
    IRecordingRepository recordings,
    EncodePlaces places,
    EncodeScratchFiles scratch,
    EncodeArtefactPlacer placer,
    EncodeScratchCleaner cleaner,
    IMachineCapabilityReader machine,
    ISourceLengthReader lengths,
    MachineSettings programmes,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeJobRunner> logger)
{
    public const int Tenths = 10;

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

        if (places.WhereTheRootIs(recording.OutputRoot) is not { } mounted)
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

        if (await scratch.RecordAsync(job, EncodeScratchKind.WorkFile, job.WorkFileName, cancellationToken) is not { } work)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.CapabilityUnavailable,
                $"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted",
                cancellationToken);
        }

        logger.LogInformation(
            "Job {Job} starts attempt {Attempt} on the {Encoder}, {Whole} of source to get through.",
            job.Id.Wire,
            job.Attempt,
            encoder,
            whole.Length is { } length ? length.ToString("c", CultureInfo.InvariantCulture) : "an unmeasured length");

        int tenthsTold = -1;
        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            programmes.Programme,
            [.. FfmpegEncodeInvocation.Arguments(recording.ServiceId, profile, encoder, source.FullName), .. FfmpegEncodeInvocation.Delivery(work)],
            whole.Length,
            settings.StalledAfter,
            progress => Tell(job, progress, ref tenthsTold),
            clock,
            cancellationToken);

        if (ran.Fault is EncodeRunFault.ProgrammeMissing)
        {
            return await RefuseAsync(job, EncodeFailure.CapabilityUnavailable, ran.Complained, cancellationToken);
        }

        if (ran.Fault is EncodeRunFault.Stalled)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.TimedOut,
                $"nothing was reported for {settings.StalledAfter}, so the programme was stopped where it stood: {ran.Complained}",
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

        EncodePlacementOutcome placed = await placer.PlaceAsync(job, cancellationToken);

        logger.LogInformation("Job {Job} ends {Status}; its artefact was {Placed}.", job.Id.Wire, job.Status, placed);

        return await SweptAsync(job, cancellationToken);
    }

    private void Tell(EncodeJob job, EncodeProgress progress, ref int tenthsTold)
    {
        if (progress.Portion is not { } portion)
        {
            return;
        }

        int tenths = (int)Math.Floor(portion * Tenths);

        if (tenths == tenthsTold && !progress.Ended)
        {
            return;
        }

        tenthsTold = tenths;
        logger.LogInformation(
            "Job {Job} is {Percent}% of the way through at {Speed}x, {Left} left.",
            job.Id.Wire,
            (int)Math.Round(portion * 100),
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
}
