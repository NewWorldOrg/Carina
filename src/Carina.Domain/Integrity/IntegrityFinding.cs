using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Integrity;

public sealed record IntegrityFinding
{
    private IntegrityFinding(
        IntegrityFault fault,
        OutputRoot root,
        string fileName,
        RecordingId? recordingId,
        long? ledgerSize,
        long? observedSize,
        DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "A finding is one the sweep can class.");
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

        Fault = fault;
        Root = root;
        FileName = fileName;
        RecordingId = recordingId;
        LedgerSize = ledgerSize;
        ObservedSize = observedSize;
        NoticedAt = UtcTimes.Required(noticedAt, nameof(noticedAt));
    }

    public IntegrityFault Fault { get; }

    public OutputRoot Root { get; }

    public string FileName { get; }

    public RecordingId? RecordingId { get; }

    public long? LedgerSize { get; }

    public long? ObservedSize { get; }

    public DateTime NoticedAt { get; }

    public static IntegrityFinding SizeDisagrees(
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        long observedSize,
        DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(fileName);

        return new IntegrityFinding(
            IntegrityFault.SizeDisagrees,
            root,
            fileName.Value,
            recordingId,
            ledgerSize,
            observedSize,
            noticedAt);
    }

    public static IntegrityFinding NoLedgerRow(
        OutputRoot root,
        string fileName,
        long observedSize,
        DateTime noticedAt)
        => new(IntegrityFault.NoLedgerRow, root, fileName, null, null, observedSize, noticedAt);

    public static IntegrityFinding FileMissing(
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(fileName);

        return new IntegrityFinding(
            IntegrityFault.FileMissing,
            root,
            fileName.Value,
            recordingId,
            ledgerSize,
            null,
            noticedAt);
    }

    public static IntegrityFinding FileEmpty(
        OutputRoot root,
        RecordingId recordingId,
        RecordingFileName fileName,
        long ledgerSize,
        long observedSize,
        DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(fileName);

        return new IntegrityFinding(
            IntegrityFault.FileEmpty,
            root,
            fileName.Value,
            recordingId,
            ledgerSize,
            observedSize,
            noticedAt);
    }
}
