namespace Carina.Domain.Integrity;

public enum StrayFileRefusal
{
    NamesARecording = 1,

    NothingOnTheDisk = 2,

    AlreadyThrownAway = 3,

    NoTimeWasTaken = 4,
}

public enum StrayFileChange
{
    Gone = 1,

    Resized = 2,

    Rewritten = 3,
}

public static class StrayFileDisposal
{
    public static StrayFileRefusal? Refusal(IntegrityFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        if (finding.Fault is IntegrityFault.FileMissing)
        {
            return StrayFileRefusal.NothingOnTheDisk;
        }

        if (!IntegrityFaults.ThatNameAFileNoRecordingOwns.Contains(finding.Fault))
        {
            return StrayFileRefusal.NamesARecording;
        }

        if (finding.ThrownAwayAt is not null)
        {
            return StrayFileRefusal.AlreadyThrownAway;
        }

        return finding.LastWrittenAt is null ? StrayFileRefusal.NoTimeWasTaken : null;
    }

    public static bool Claimed(
        IntegrityFinding finding,
        IReadOnlyList<LedgerFile> ledger,
        IReadOnlyList<DeclaredFile> declared)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(declared);

        return ledger.Any(row => row.Root.Equals(finding.Root)
                                 && string.Equals(row.FileName.Value, finding.Path, StringComparison.Ordinal))
               || declared.Any(file => file.Root.Equals(finding.Root)
                                       && string.Equals(file.Path, finding.Path, StringComparison.Ordinal));
    }

    public static StrayFileChange? Change(IntegrityFinding finding, StoredFile? now)
    {
        ArgumentNullException.ThrowIfNull(finding);

        if (now is null)
        {
            return StrayFileChange.Gone;
        }

        if (now.SizeBytes != finding.ObservedSize)
        {
            return StrayFileChange.Resized;
        }

        return now.LastWrittenAt == finding.LastWrittenAt ? null : StrayFileChange.Rewritten;
    }
}
