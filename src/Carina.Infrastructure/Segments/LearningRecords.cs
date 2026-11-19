using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Segments;

public enum ExtractionChange
{
    Written = 1,

    Declined = 2,

    Missing = 3,

    KeptMoving = 4,

    AnotherIsReading = 5,
}

public sealed class LearningRecords(IServiceScopeFactory scopes)
{
    public const int Attempts = 5;

    public async Task<LearningExtraction?> FindAsync(RecordingId id, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await Records(scope).FindAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<LearningExtraction>> ListAsync(
        LearningExtractionState state,
        int limit,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await Records(scope).ListAsync(state, limit, cancellationToken);
    }

    public async Task AddAsync(LearningExtraction extraction, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        await Records(scope).AddAsync(extraction, cancellationToken);
    }

    public async Task<ExtractionChange> ChangeAsync(
        RecordingId id,
        Func<LearningExtraction, bool> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            ILearningExtractionRepository records = Records(scope);
            LearningExtraction? record = await records.FindAsync(id, cancellationToken);

            if (record is null)
            {
                return ExtractionChange.Missing;
            }

            if (!change(record))
            {
                return ExtractionChange.Declined;
            }

            if (await SavedAsync(records, record, cancellationToken) is { } saved)
            {
                return saved;
            }
        }

        return ExtractionChange.KeptMoving;
    }

    public async Task KeepAsync(
        RecordingId id,
        IReadOnlyList<LearningDataChunk> chunks,
        ExtractionVersion version,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ILearningDataRepository data = scope.ServiceProvider.GetRequiredService<ILearningDataRepository>();

        foreach (LearningDataChunk chunk in chunks)
        {
            foreach (LearningDataKind kind in LearningDataChunk.Kinds)
            {
                await data.KeepAsync(LearningDataBlock.Of(id, LearningDataPart.Of(chunk, kind), version, at), cancellationToken);
            }
        }
    }

    public async Task KeepPartsAsync(
        RecordingId id,
        IReadOnlyList<LearningDataPart> parts,
        ExtractionVersion version,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parts);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ILearningDataRepository data = scope.ServiceProvider.GetRequiredService<ILearningDataRepository>();

        foreach (LearningDataPart part in parts)
        {
            await data.KeepAsync(LearningDataBlock.Of(id, part, version, at), cancellationToken);
        }
    }

    private static async Task<ExtractionChange?> SavedAsync(
        ILearningExtractionRepository records,
        LearningExtraction record,
        CancellationToken cancellationToken)
    {
        try
        {
            return await records.SaveAsync(record, cancellationToken) is LearningExtractionWrite.AnotherIsReading
                ? ExtractionChange.AnotherIsReading
                : ExtractionChange.Written;
        }
        catch (LearningExtractionMovedMeanwhileException)
        {
            return null;
        }
    }

    private static ILearningExtractionRepository Records(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ILearningExtractionRepository>();
}
