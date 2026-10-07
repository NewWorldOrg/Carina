using System.Runtime.CompilerServices;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.TestSupport;

/// <summary>
/// The records of taking the learning data out, held in memory the way the table holds them: every
/// read hands back a copy of the row as it stands, and a copy saved after the row changed since it
/// was read is refused.
/// </summary>
public sealed class HeldLearningExtractions : ILearningExtractionRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<RecordingId, (LearningExtraction Row, int Version)> rows = [];

    private readonly ConditionalWeakTable<LearningExtraction, StrongBox<int>> readAt = new();

    /// <summary>
    /// Asked before each save; when it says so, the row is changed by something else first.
    /// </summary>
    public Func<RecordingId, bool>? MovesBeforeSaving { get; set; }

    public int Refused { get; private set; }

    public int Saves { get; private set; }

    public LearningExtraction? Row(RecordingId id)
    {
        lock (gate)
        {
            return rows.TryGetValue(id, out (LearningExtraction Row, int Version) held) ? Copied(held.Row) : null;
        }
    }

    public IReadOnlyList<LearningExtraction> All()
    {
        lock (gate)
        {
            return [.. rows.Values.Select(held => Copied(held.Row))];
        }
    }

    public void Hold(LearningExtraction extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        lock (gate)
        {
            rows[extraction.RecordingId] = (Copied(extraction), rows.TryGetValue(extraction.RecordingId, out (LearningExtraction Row, int Version) held) ? held.Version + 1 : 1);
        }
    }

    public Task<LearningExtraction?> FindAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(rows.TryGetValue(recordingId, out (LearningExtraction Row, int Version) held) ? Read(held) : null);
        }
    }

    public Task<IReadOnlyList<LearningExtraction>> ListAsync(
        LearningExtractionState state,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<LearningExtraction>>(
            [
                .. rows.Values
                    .Where(held => held.Row.State == state)
                    .OrderByDescending(held => held.Row.Programme.RecordingStartedAt)
                    .Take(limit)
                    .Select(Read),
            ]);
        }
    }

    public Task AddAsync(LearningExtraction extraction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        lock (gate)
        {
            if (!rows.TryAdd(extraction.RecordingId, (Copied(extraction), 1)))
            {
                throw new InvalidOperationException("A record is already kept for this recording.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<LearningExtractionWrite> SaveAsync(LearningExtraction extraction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        bool moves = MovesBeforeSaving?.Invoke(extraction.RecordingId) ?? false;

        lock (gate)
        {
            (LearningExtraction row, int version) = rows[extraction.RecordingId];
            int current = moves ? version + 1 : version;

            rows[extraction.RecordingId] = (row, current);

            if (!readAt.TryGetValue(extraction, out StrongBox<int>? read) || read.Value != current)
            {
                Refused++;

                throw new LearningExtractionMovedMeanwhileException(extraction.RecordingId);
            }

            rows[extraction.RecordingId] = (Copied(extraction), current + 1);
            Saves++;
        }

        return Task.FromResult(LearningExtractionWrite.Written);
    }

    private LearningExtraction Read((LearningExtraction Row, int Version) held)
    {
        LearningExtraction copy = Copied(held.Row);

        readAt.AddOrUpdate(copy, new StrongBox<int>(held.Version));

        return copy;
    }

    private static LearningExtraction Copied(LearningExtraction row)
        => LearningExtraction.Rehydrate(
            row.RecordingId,
            row.State,
            row.Version,
            row.ReadThrough,
            [.. row.Gaps],
            row.Sound,
            row.Failure,
            row.Failures,
            row.Programme,
            row.CreatedAt,
            row.UpdatedAt);
}

/// <summary>
/// The learning data held in memory, one block per recording, kind and chunk, a block written again
/// taking the place of the one before.
/// </summary>
public sealed class HeldLearningData : ILearningDataRepository
{
    private readonly Lock gate = new();

    private readonly Dictionary<(RecordingId Recording, LearningDataKind Kind, int Chunk), LearningDataBlock> blocks = [];

    public int Writes { get; private set; }

    public IReadOnlyList<LearningDataBlock> Of(RecordingId recordingId)
    {
        lock (gate)
        {
            return [.. blocks.Values.Where(block => block.RecordingId.Equals(recordingId)).OrderBy(block => block.Kind).ThenBy(block => block.Chunk)];
        }
    }

    public Task KeepAsync(LearningDataBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);

        lock (gate)
        {
            blocks[(block.RecordingId, block.Kind, block.Chunk)] = block;
            Writes++;
        }

        return Task.CompletedTask;
    }

    public Task<LearningDataBlock?> FindAsync(
        RecordingId recordingId,
        LearningDataKind kind,
        int chunk,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(blocks.GetValueOrDefault((recordingId, kind, chunk)));
        }
    }

    public Task<int> CountAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(blocks.Keys.Count(key => key.Recording.Equals(recordingId)));
        }
    }

    public Task<long> SizeAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(blocks.Values.Sum(block => (long)block.Bytes.Length));
        }
    }
}
