using Carina.Domain.Encodings;
using Carina.Domain.Integrity;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Confirms at startup that a rename from where a job works to where its artefact goes is a rename
/// (A-エンコード-024), for each root this process holds for writing. The roots the recordings are
/// read from are not looked at: nothing is ever written into them. A working directory on another
/// mount than a held root stops the process, because every job into that root would otherwise end
/// in a copy that an interruption makes look complete. A held root this process cannot write, and
/// a process that holds no root at all, are reported and left to the jobs to refuse one by one.
/// </summary>
public sealed class EncodeMountCheck(
    EncodeSettings settings,
    IRenameProbe probe,
    ILogger<EncodeMountCheck> logger) : IHostedService
{
    public const string Setting = "Encodings:WorkedIn";

    public const string RootSetting = "Encodings:OutputRoots";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!settings.HoldsAnyRoot)
        {
            logger.LogWarning(
                "{Setting} names no root, so nothing can be encoded: an artefact is placed only in a root this process holds for writing, never in one the recordings are read from.",
                RootSetting);

            return Task.CompletedTask;
        }

        if (settings.WorkedIn is not { } workedIn)
        {
            logger.LogInformation(
                "{Setting} names nothing, so a work file is written beside the artefact it becomes and no rename can cross a mount.",
                Setting);

            foreach (StorageRootPath root in settings.OutputRoots)
            {
                Report(root, probe.Probe(root.Path, root.Path));
            }

            return Task.CompletedTask;
        }

        if (!Directory.Exists(workedIn))
        {
            throw new InvalidOperationException(
                $"{Setting} names '{workedIn}', and there is no directory there. A working directory that is a mount point "
                + "nobody mounted would fill the disk underneath it, so this process does not start.");
        }

        foreach (StorageRootPath root in settings.OutputRoots)
        {
            RenameVerdict verdict = probe.Probe(workedIn, root.Path);

            switch (verdict.Standing)
            {
                case RenameStanding.WouldCrossAMount:
                    throw new InvalidOperationException(
                        $"{Setting} is '{workedIn}', which is on a different mount from encode root '{root.Root.Value}' at "
                        + $"'{root.Path}'. A work file renamed across mounts is copied instead, and an interruption then looks "
                        + $"like success, so this process does not start. Name a directory on the same mount, or leave "
                        + $"{Setting} unset to write beside the artefact.");

                case RenameStanding.CannotWriteFrom:
                    throw new InvalidOperationException(
                        $"{Setting} is '{workedIn}', and this process cannot write there: {verdict.Note}");

                default:
                    Report(root, verdict);

                    break;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Report(StorageRootPath root, RenameVerdict verdict)
    {
        if (verdict.IsARename)
        {
            logger.LogInformation(
                "Encode root {Root} at {Path} takes a work file by rename.",
                root.Root.Value,
                root.Path);

            return;
        }

        logger.LogWarning(
            "Encode root {Root} at {Path} cannot be written by this process, so nothing can be encoded into it: {Note}",
            root.Root.Value,
            root.Path,
            verdict.Note);
    }
}
