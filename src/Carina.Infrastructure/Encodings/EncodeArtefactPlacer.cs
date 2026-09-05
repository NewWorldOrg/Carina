using Carina.Domain.Encodings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public enum EncodePlacementOutcome
{
    Moved = 1,

    Reconfirmed = 2,

    Collided = 3,

    Refused = 4,
}

/// <summary>
/// Turns a finished work file into the artefact, in the order BR-ED2-009 fixes: the name is worked
/// out now and written into the ledger, and only then is the file looked at and moved. Whatever is
/// already at that name is this job's own earlier success if the ledger said so before this
/// attempt, and a collision otherwise — and a collision is a failure, never an overwrite.
/// </summary>
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
        string work = Path.Combine(workshop, job.WorkFileName.Value);
        string artefact = Path.Combine(room, candidate.Value);

        if (await jobs.ClaimArtefactAsync(job, candidate, cancellationToken) is ArtefactClaim.TakenByAnother)
        {
            await RefuseAsync(
                job,
                EncodePlacements.WhatACollisionIsCalled,
                $"another job already holds '{candidate.Value}' under output root '{job.OutputRoot.Value}'",
                cancellationToken);

            return EncodePlacementOutcome.Collided;
        }

        switch (EncodePlacements.Judge(File.Exists(artefact), hadAlreadyClaimed))
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
                return await MoveAsync(job, work, artefact, workshop, room, cancellationToken);
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
        CancellationToken cancellationToken)
    {
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
            File.Move(work, artefact, overwrite: false);
        }
        catch (IOException refusal) when (refusal.HResult is NoSpaceLeft)
        {
            return await RefuseAsync(job, EncodeFailure.NotEnoughRoom, refusal.Message, cancellationToken);
        }
        catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(artefact))
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

        return EncodePlacementOutcome.Moved;
    }

    private async Task SettleTheWorkFileAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeScratchFile> owed = await ledger.ListOwedAsync(job.Id, cancellationToken);
        EncodeScratchFile? workFile = owed.FirstOrDefault(scratch =>
            scratch.Kind is EncodeScratchKind.WorkFile && scratch.FileName.Equals(job.WorkFileName));

        if (workFile is null)
        {
            logger.LogWarning(
                "Job {Job} placed its artefact from a work file the ledger never recorded: {File}.",
                job.Id.Wire,
                job.WorkFileName.Value);

            return;
        }

        workFile.Settle(EncodeScratchFate.BecameTheArtefact, Now());
        await ledger.SaveAsync(workFile, cancellationToken);
    }

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
