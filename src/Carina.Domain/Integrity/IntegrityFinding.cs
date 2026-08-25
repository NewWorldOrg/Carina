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

    public static IntegrityFinding Rehydrate(
        IntegrityFindingId id,
        IntegrityCheckId checkId,
        IntegrityFault fault,
        OutputRoot root,
        string path,
        RecordingId? recordingId,
        long? ledgerSize,
        long? observedSize,
        DateTime noticedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(checkId);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "A finding is one the sweep can class.");
        }

        if (path.Length > StoredFile.MaxPathLength)
        {
            throw new ArgumentException(
                $"A path under an output root is at most {StoredFile.MaxPathLength} characters, "
                + $"and this one has {path.Length}.",
                nameof(path));
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
            Fault = fault,
            Root = root,
            Path = path,
            RecordingId = recordingId,
            LedgerSize = ledgerSize,
            ObservedSize = observedSize,
            NoticedAt = UtcTimes.Required(noticedAt, nameof(noticedAt)),
        };
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
        DateTime noticedAt)
        => Rehydrate(
            IntegrityFindingId.New(),
            checkId,
            IntegrityFault.NoLedgerRow,
            root,
            path,
            null,
            null,
            observedSize,
            noticedAt);

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
            IntegrityFindingId.New(),
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
