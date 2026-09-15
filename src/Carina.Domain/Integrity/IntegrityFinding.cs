using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed class IntegrityFinding
{
    private IntegrityFinding()
    {
    }

    public IntegrityFindingId Id { get; private set; } = null!;

    public IntegrityCheckId CheckId { get; private set; } = null!;

    public IntegrityFault Fault { get; private set; }

    public OutputRoot Root { get; private set; } = null!;

    public string Path { get; private set; } = string.Empty;

    public RecordingId? RecordingId { get; private set; }

    public long? LedgerSize { get; private set; }

    public long? ObservedSize { get; private set; }

    public DateTime NoticedAt { get; private set; }

    public DateTime? LastWrittenAt { get; private set; }

    public DateTime? ThrownAwayAt { get; private set; }

    public static IntegrityFinding Rehydrate(
        IntegrityFindingId id,
        IntegrityCheckId checkId,
        IntegrityFault fault,
        OutputRoot root,
        string path,
        RecordingId? recordingId,
        long? ledgerSize,
        long? observedSize,
        DateTime noticedAt,
        DateTime? lastWrittenAt = null,
        DateTime? thrownAwayAt = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(checkId);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(path);

        bool noRecordingOwnsIt = IntegrityFaults.ThatNameAFileNoRecordingOwns.Contains(IntegrityFaults.Named(fault));
        DateTime noticed = UtcTimes.Required(noticedAt, nameof(noticedAt));

        if (lastWrittenAt is not null && !noRecordingOwnsIt)
        {
            throw new ArgumentException(
                $"Only a file no recording owns keeps when it was last written, and a {fault} finding is not one.",
                nameof(lastWrittenAt));
        }

        if (thrownAwayAt is not null && (!noRecordingOwnsIt || lastWrittenAt is null))
        {
            throw new ArgumentException(
                "Only a file no recording owns, whose last write was kept, is ever thrown away from a finding.",
                nameof(thrownAwayAt));
        }

        if (thrownAwayAt is { } thrown && thrown < noticed)
        {
            throw new ArgumentException(
                "A file is thrown away after it was found, never before.",
                nameof(thrownAwayAt));
        }

        if (ledgerSize is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ledgerSize), ledgerSize, "A file is not smaller than empty.");
        }

        if (observedSize is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedSize),
                observedSize,
                "A file is not smaller than empty.");
        }

        return new IntegrityFinding
        {
            Id = id,
            CheckId = checkId,
            Fault = IntegrityFaults.Named(fault),
            Root = root,
            Path = path,
            RecordingId = recordingId,
            LedgerSize = ledgerSize,
            ObservedSize = observedSize,
            NoticedAt = noticed,
            LastWrittenAt = lastWrittenAt is { } written
                ? StrayFileStamp.Truncated(UtcTimes.Required(written, nameof(lastWrittenAt)))
                : null,
            ThrownAwayAt = UtcTimes.Optional(thrownAwayAt, nameof(thrownAwayAt)),
        };
    }

    public void ThrowAway(DateTime at)
    {
        if (StrayFileDisposal.Refusal(this) is { } refusal)
        {
            throw new InvalidOperationException(
                $"The file this finding names is not one to throw away from here: {refusal}.");
        }

        DateTime thrown = UtcTimes.Required(at, nameof(at));

        if (thrown < NoticedAt)
        {
            throw new ArgumentException("A file is thrown away after it was found, never before.", nameof(at));
        }

        ThrownAwayAt = thrown;
    }

    public static IntegrityFinding SizeDisagrees(
        IntegrityCheckId checkId,
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        long observedSize,
        DateTime noticedAt)
        => About(
            IntegrityFault.SizeDisagrees,
            checkId,
            root,
            recordingId,
            fileName,
            ledgerSize,
            observedSize,
            noticedAt);

    public static IntegrityFinding FileEmpty(
        IntegrityCheckId checkId,
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        long observedSize,
        DateTime noticedAt)
        => About(
            IntegrityFault.FileEmpty,
            checkId,
            root,
            recordingId,
            fileName,
            ledgerSize,
            observedSize,
            noticedAt);

    public static IntegrityFinding EmptyThoughComplete(
        IntegrityCheckId checkId,
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        long observedSize,
        DateTime noticedAt)
        => About(
            IntegrityFault.EmptyThoughComplete,
            checkId,
            root,
            recordingId,
            fileName,
            ledgerSize,
            observedSize,
            noticedAt);

    public static IntegrityFinding FileMissing(
        IntegrityCheckId checkId,
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        DateTime noticedAt)
        => About(
            IntegrityFault.FileMissing,
            checkId,
            root,
            recordingId,
            fileName,
            ledgerSize,
            null,
            noticedAt);

    public static IntegrityFinding NoLedgerRow(
        IntegrityCheckId checkId,
        OutputRoot root,
        string path,
        long observedSize,
        DateTime noticedAt,
        DateTime? lastWrittenAt = null)
        => Rehydrate(
            IntegrityFindingId.Of(IntegrityFault.NoLedgerRow, root, path, null),
            checkId,
            IntegrityFault.NoLedgerRow,
            root,
            path,
            null,
            null,
            observedSize,
            noticedAt,
            lastWrittenAt);

    private static IntegrityFinding About(
        IntegrityFault fault,
        IntegrityCheckId checkId,
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        long? observedSize,
        DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(fileName);

        return Rehydrate(
            IntegrityFindingId.Of(fault, root, fileName.Value, recordingId),
            checkId,
            fault,
            root,
            fileName.Value,
            recordingId,
            ledgerSize,
            observedSize,
            noticedAt);
    }
}
