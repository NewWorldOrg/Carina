using Carina.Domain.Encodings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public enum EncodePlacementOutcome
{
    Moved = 1,

    Reconfirmed = 2,

    Collided = 3,

    Refused = 4,

    Replaced = 5,
}

/// <summary>
/// Turns a finished work file into the artefact.
/// </summary>
/// <remarks>
/// The name is worked out and written into the ledger first, and only then is the file looked at and
/// moved. A file already at that name is this job's own earlier success if the ledger said so before
/// this attempt, and a collision, which fails the job, otherwise. For a job asked to make the
/// artefact again, the earlier job gives the name up, this job claims it, and the artefact is then
/// replaced by a single rename; nothing before the rename touches the existing artefact.
/// </remarks>
public sealed class EncodeArtefactPlacer(
    IEncodeJobRepository jobs,
    IEncodeScratchLedger ledger,
    EncodePlaces places,
    IRenameProbe probe,
    TimeProvider clock,
    ILogger<EncodeArtefactPlacer> logger)
{
    public const int NoSpaceLeft = 28;

    public async Task<EncodePlacementOutcome> PlaceAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status is not EncodeJobStatus.Running)
        {
            throw new InvalidOperationException($"Only a running job places its artefact, and this one stands at {job.Status}.");
        }

        if (places.WhereTheArtefactGoes(job.OutputRoot) is not { } room
            || places.WhereTheWorkGoes(job.OutputRoot) is not { } workshop)
        {
            return await RefuseAsync(
                job,
                EncodeFailure.CapabilityUnavailable,
                $"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted",
                cancellationToken);
        }

        EncodeFileName candidate = EncodeFileName.Artefact(job.RecordingId, job.ProfileId);
        bool hadAlreadyClaimed = candidate.Equals(job.ArtefactName);
        string work = Path.Combine(workshop, job.FileToPlace.Value);
        string artefact = Path.Combine(room, candidate.Value);

        if (job.MakesItAgain && !hadAlreadyClaimed)
        {
            int gaveItUp = await jobs.TakeTheNameOverAsync(job, candidate, Now(), cancellationToken);

            logger.LogInformation(
                "Job {Job} was asked to make the artefact again, and took '{Artefact}' over from {Earlier} earlier job(s).",
                job.Id.Wire,
                candidate.Value,
                gaveItUp);
        }

        if (await jobs.ClaimArtefactAsync(job, candidate, cancellationToken) is ArtefactClaim.TakenByAnother)
        {
            await RefuseAsync(
                job,
                EncodePlacements.WhatACollisionIsCalled,
                $"another job already holds '{candidate.Value}' under output root '{job.OutputRoot.Value}'",
                cancellationToken);

            return EncodePlacementOutcome.Collided;
        }

        EncodePlacementVerdict verdict = EncodePlacements.Judge(
            File.Exists(artefact),
            hadAlreadyClaimed,
            job.MakesItAgain && File.Exists(work));

        switch (verdict)
        {
            case EncodePlacementVerdict.Collision:
                await RefuseAsync(
                    job,
                    EncodePlacements.WhatACollisionIsCalled,
                    $"something no job wrote is already at '{candidate.Value}' under output root '{job.OutputRoot.Value}', and it is left as it is",
                    cancellationToken);

                return EncodePlacementOutcome.Collided;

            case EncodePlacementVerdict.Reconfirm:
                return await ReconfirmAsync(job, artefact, candidate, cancellationToken);

            default:
                return await MoveAsync(job, work, artefact, workshop, room, verdict, cancellationToken);
        }
    }

    private async Task<EncodePlacementOutcome> ReconfirmAsync(
        EncodeJob job,
        string artefact,
        EncodeFileName candidate,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(artefact).Length is 0)
        {
            await RefuseAsync(
                job,
                EncodePlacements.WhatACollisionIsCalled,
                $"the file at this job's own name '{candidate.Value}' is empty, and it is left as it is",
                cancellationToken);

            return EncodePlacementOutcome.Collided;
        }

        logger.LogInformation(
            "Job {Job} found its artefact {Artefact} already in place from an earlier attempt, and kept it.",
            job.Id.Wire,
            candidate.Value);

        job.Complete(Now());
        await jobs.SaveAsync(job, cancellationToken);

        return EncodePlacementOutcome.Reconfirmed;
    }

    private async Task<EncodePlacementOutcome> MoveAsync(
        EncodeJob job,
        string work,
        string artefact,
        string workshop,
        string room,
        EncodePlacementVerdict verdict,
        CancellationToken cancellationToken)
    {
        bool replacing = verdict is EncodePlacementVerdict.Replace;

        if (!File.Exists(work))
        {
            throw new InvalidOperationException(
                $"Job {job.Id.Wire} has nothing to place: its work file for attempt {job.Attempt} is not where it was to be written.");
        }

        RenameVerdict rename = probe.Probe(workshop, room);

        if (!rename.IsARename)
        {
            return await RefuseAsync(job, EncodeFailure.CapabilityUnavailable, Because(rename, job), cancellationToken);
        }

        try
        {
            Move(work, artefact, replacing);
        }
        catch (IOException refusal) when (refusal.HResult is NoSpaceLeft)
        {
            return await RefuseAsync(job, EncodeFailure.NotEnoughRoom, refusal.Message, cancellationToken);
        }
        catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException)
        {
            if (!replacing && File.Exists(artefact))
            {
                await RefuseAsync(
                    job,
                    EncodePlacements.WhatACollisionIsCalled,
                    $"something arrived at '{Path.GetFileName(artefact)}' under output root '{job.OutputRoot.Value}' while this job was placing its own, and it is left as it is",
                    cancellationToken);

                return EncodePlacementOutcome.Collided;
            }

            return await RefuseAsync(job, EncodeFailure.CapabilityUnavailable, refusal.Message, cancellationToken);
        }

        job.Complete(Now());
        await jobs.SaveAsync(job, cancellationToken);
        await SettleTheWorkFileAsync(job, cancellationToken);

        return replacing ? EncodePlacementOutcome.Replaced : EncodePlacementOutcome.Moved;
    }

    private async Task SettleTheWorkFileAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeScratchFile> owed = await ledger.ListOwedAsync(job.Id, cancellationToken);
        EncodeScratchFile? workFile = owed.FirstOrDefault(scratch =>
            scratch.Kind is EncodeScratchKind.WorkFile or EncodeScratchKind.CaptionedWork
            && scratch.FileName.Equals(job.FileToPlace));

        if (workFile is null)
        {
            logger.LogWarning(
                "Job {Job} placed its artefact from a work file the ledger never recorded: {File}.",
                job.Id.Wire,
                job.FileToPlace.Value);

            return;
        }

        workFile.Settle(EncodeScratchFate.BecameTheArtefact, Now());
        await ledger.SaveAsync(workFile, cancellationToken);
    }

    /// <summary>
    /// Puts a copy of a standing artefact with a text track of captions put in where the artefact stands, by a
    /// single rename over it from where the job's work goes. The artefact's name is the job's in the ledger
    /// already; nothing before the rename touches the artefact, and a rename that cannot be made leaves it as
    /// it was.
    /// </summary>
    public RenameVerdict PutCaptionedInPlace(EncodeJob job, string captioned)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrEmpty(captioned);

        if (!job.StandsAsTheArtefact)
        {
            throw new InvalidOperationException($"Only an artefact that stands has a text track put into it, and job {job.Id.Wire} stands at {job.Status}.");
        }

        if (places.WhereTheArtefactGoes(job.OutputRoot) is not { } room || places.WhereTheWorkGoes(job.OutputRoot) is not { } workshop)
        {
            return new RenameVerdict(RenameStanding.CannotWriteTo, $"nothing tells this process where output root '{job.OutputRoot.Value}' is mounted");
        }

        RenameVerdict rename = probe.Probe(workshop, room);

        if (rename.IsARename)
        {
            Move(captioned, Path.Combine(room, job.ArtefactName!.Value), replacing: true);
        }

        return rename;
    }

    private static void Move(string work, string artefact, bool replacing) => File.Move(work, artefact, overwrite: replacing);

    private async Task<EncodePlacementOutcome> RefuseAsync(
        EncodeJob job,
        EncodeFailure failure,
        string note,
        CancellationToken cancellationToken)
    {
        job.Fail(failure, note, Now());
        await jobs.SaveAsync(job, cancellationToken);

        return EncodePlacementOutcome.Refused;
    }

    private static string Because(RenameVerdict rename, EncodeJob job)
        => rename.Standing switch
        {
            RenameStanding.WouldCrossAMount =>
                $"the working directory and output root '{job.OutputRoot.Value}' are on different mounts, so a rename would have degraded to a copy; the work file is left where it is",
            RenameStanding.CannotWriteFrom =>
                $"the working directory for output root '{job.OutputRoot.Value}' cannot be written by this process: {rename.Note}",
            _ => $"output root '{job.OutputRoot.Value}' cannot be written by this process: {rename.Note}",
        };

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
