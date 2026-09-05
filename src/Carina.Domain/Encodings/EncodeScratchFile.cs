using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public enum EncodeScratchKind
{
    WorkFile = 1,

    Chapters = 2,
}

public enum EncodeScratchFate
{
    Removed = 1,

    AlreadyGone = 2,

    BecameTheArtefact = 3,

    CouldNotBeRemoved = 4,
}

public static class EncodeScratchShapes
{
    public static EncodeScratchKind Named(EncodeScratchKind kind)
        => Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "A scratch file is one of the kinds a job writes.");

    public static EncodeScratchFate Named(EncodeScratchFate fate)
        => Enum.IsDefined(fate)
            ? fate
            : throw new ArgumentOutOfRangeException(nameof(fate), fate, "A scratch file ends in one of the ways named here.");
}

/// <summary>
/// A file a job writes on the way and owes a removal for. It is written into the ledger before it
/// exists on disk, and when the job has ended the ledger — never a walk of the directory — says
/// what there is to remove (BR-ED2-010).
/// </summary>
public sealed class EncodeScratchFile
{
    private EncodeScratchFile()
    {
    }

    public EncodeScratchFileId Id { get; private set; } = null!;

    public EncodeJobId JobId { get; private set; } = null!;

    public EncodeScratchKind Kind { get; private set; }

    public OutputRoot OutputRoot { get; private set; } = null!;

    public EncodeFileName FileName { get; private set; } = null!;

    public DateTime WrittenAt { get; private set; }

    public DateTime? RemovedAt { get; private set; }

    public EncodeScratchFate? Fate { get; private set; }

    public bool IsOwedARemoval => RemovedAt is null;

    public static EncodeScratchFile Record(
        EncodeScratchFileId id,
        EncodeJobId jobId,
        EncodeScratchKind kind,
        OutputRoot outputRoot,
        EncodeFileName fileName,
        DateTime at)
        => Rehydrate(id, jobId, kind, outputRoot, fileName, at, null, null);

    public static EncodeScratchFile Rehydrate(
        EncodeScratchFileId id,
        EncodeJobId jobId,
        EncodeScratchKind kind,
        OutputRoot outputRoot,
        EncodeFileName fileName,
        DateTime writtenAt,
        DateTime? removedAt,
        EncodeScratchFate? fate)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(fileName);

        if ((removedAt is null) != (fate is null))
        {
            throw new ArgumentException("A removal is settled with a time and a fate together, or not at all.", nameof(fate));
        }

        return new EncodeScratchFile
        {
            Id = id,
            JobId = jobId,
            Kind = EncodeScratchShapes.Named(kind),
            OutputRoot = outputRoot,
            FileName = fileName,
            WrittenAt = UtcTimes.Required(writtenAt, nameof(writtenAt)),
            RemovedAt = UtcTimes.Optional(removedAt, nameof(removedAt)),
            Fate = fate is { } settled ? EncodeScratchShapes.Named(settled) : null,
        };
    }

    public void Settle(EncodeScratchFate fate, DateTime at)
    {
        if (!IsOwedARemoval)
        {
            throw new InvalidOperationException($"This scratch file was already settled as {Fate}.");
        }

        EncodeScratchFate named = EncodeScratchShapes.Named(fate);
        DateTime when = UtcTimes.Required(at, nameof(at));

        if (when < WrittenAt)
        {
            throw new ArgumentOutOfRangeException(nameof(at), at, "A file is removed after it was written, not before.");
        }

        Fate = named;
        RemovedAt = when;
    }
}
