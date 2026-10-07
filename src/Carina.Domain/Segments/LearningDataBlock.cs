using System.Diagnostics.CodeAnalysis;

using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// One kind of the learning data of one chunk of one recording, kept as the bytes
/// <see cref="LearningDataFormat"/> writes, with the version that made it. It names the recording
/// by value.
/// </summary>
public sealed class LearningDataBlock
{
    private LearningDataBlock()
    {
    }

    public RecordingId RecordingId { get; private set; } = null!;

    public LearningDataKind Kind { get; private set; }

    public int Chunk { get; private set; }

    public byte[] Bytes { get; private set; } = [];

    public ExtractionVersion Version { get; private set; } = null!;

    public DateTime WrittenAt { get; private set; }

    public static LearningDataBlock Of(
        RecordingId recordingId,
        LearningDataPart part,
        ExtractionVersion version,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(part);

        return Rehydrate(recordingId, part.Kind, part.Index, LearningDataFormat.Write(part), version, at);
    }

    public static LearningDataBlock Rehydrate(
        RecordingId recordingId,
        LearningDataKind kind,
        int chunk,
        byte[] bytes,
        ExtractionVersion version,
        DateTime writtenAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(version);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A block holds one of the kinds of learning data.");
        }

        if (chunk is < 0 or > LearningData.LastChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(chunk), chunk, "A chunk is counted from zero, and no further than a count of seconds reaches.");
        }

        if (bytes.Length == 0)
        {
            throw new ArgumentException("A block holds the bytes of its data.", nameof(bytes));
        }

        return new LearningDataBlock
        {
            RecordingId = recordingId,
            Kind = kind,
            Chunk = chunk,
            Bytes = bytes,
            Version = version,
            WrittenAt = UtcTimes.Required(writtenAt, nameof(writtenAt)),
        };
    }

    public bool TryRead([NotNullWhen(true)] out LearningDataPart? part)
    {
        if (LearningDataFormat.TryRead(Bytes, Kind, out LearningDataPart? read) && read.Index == Chunk)
        {
            part = read;

            return true;
        }

        part = null;

        return false;
    }
}
