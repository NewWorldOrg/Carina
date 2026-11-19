using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

public sealed record TrainingMaterial(IReadOnlyList<LearningRecording> Recordings, LearningCatalog Catalog);

public sealed record Trained(SegmentLearned? Learned, bool Stopped)
{
    public static Trained NothingToLearnFrom { get; } = new(null, false);

    public static Trained StoppedPartway { get; } = new(null, true);
}

public interface ISegmentTrainer
{
    Task<Trained> TrainAsync(
        SegmentModelVersion active,
        DateTime now,
        Func<CancellationToken, Task<bool>> mayGoOn,
        CancellationToken cancellationToken);
}

public sealed class SegmentTrainer(
    SegmentDetecting detecting,
    ITrainingWorklist candidates,
    IDetectionWorklist worklist,
    ICatalogRepository catalog,
    ICatalogWorklist programmes,
    ISkipEventRepository skips,
    ISeekEventRepository seeks,
    LowestPriorityScheduler lowest,
    SegmentTrainingSettings settings,
    ILogger<SegmentTrainer> logger) : ISegmentTrainer
{
    public async Task<Trained> TrainAsync(
        SegmentModelVersion active,
        DateTime now,
        Func<CancellationToken, Task<bool>> mayGoOn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mayGoOn);

        if (await GatherAsync(active, now, mayGoOn, cancellationToken) is not { } material)
        {
            return Trained.StoppedPartway;
        }

        if (material.Recordings.Count is 0)
        {
            return Trained.NothingToLearnFrom;
        }

        SegmentSearch search = settings.Search;
        SegmentLearningRun run = await lowest
            .Run(() => SegmentLearning.Begin(material.Recordings, material.Catalog, active, search, now), cancellationToken)
            .WaitAsync(cancellationToken);
        bool done = false;

        while (!done)
        {
            done = await lowest.Run(() => run.Step(cancellationToken), cancellationToken).WaitAsync(cancellationToken);

            if (!await mayGoOn(cancellationToken))
            {
                return Trained.StoppedPartway;
            }
        }

        return new Trained(await lowest.Run(run.Result, cancellationToken).WaitAsync(cancellationToken), false);
    }

    public async Task<TrainingMaterial?> GatherAsync(
        SegmentModelVersion active,
        DateTime now,
        Func<CancellationToken, Task<bool>> mayGoOn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(mayGoOn);

        RecordingId[] chosen = [.. LearningSelection.Choose(await candidates.CandidatesAsync(cancellationToken))];

        if (chosen.Length is 0)
        {
            return new TrainingMaterial([], LearningCatalog.Empty);
        }

        ILookup<RecordingId, ViewerAction> actions = (await skips.ListAsync(chosen, cancellationToken)).ToLookup(skip => skip.RecordingId, skip => skip.Action());
        ILookup<RecordingId, ViewerSeek> sought = (await seeks.ListAsync(chosen, cancellationToken)).ToLookup(seek => seek.RecordingId, seek => seek.Seek());
        List<LearningRecording> recordings = [];

        foreach (LearningExtraction record in (await worklist.RecordsAsync(chosen, cancellationToken)).Where(record => record.IsLearnedFrom))
        {
            if (await ReadAsync(record, active, now, [.. actions[record.RecordingId]], [.. sought[record.RecordingId]], cancellationToken) is { } read)
            {
                recordings.Add(read);
            }

            if (!await mayGoOn(cancellationToken))
            {
                return null;
            }
        }

        IReadOnlyList<CatalogEntry> entries = await catalog.ListEntriesAsync(cancellationToken);
        IReadOnlyList<CatalogOccurrence> occurrences = entries.Count is 0
            ? []
            : await catalog.OccurrencesOfAsync([.. entries.Select(entry => entry.Id)], cancellationToken);

        return new TrainingMaterial(recordings, await CatalogAsync(entries, occurrences, cancellationToken));
    }

    private async Task<LearningRecording?> ReadAsync(
        LearningExtraction record,
        SegmentModelVersion active,
        DateTime now,
        IReadOnlyList<ViewerAction> actions,
        IReadOnlyList<ViewerSeek> sought,
        CancellationToken cancellationToken)
    {
        try
        {
            DetectionInputs inputs = await detecting.GatherAsync(record, active.Number, now, cancellationToken);

            return new LearningRecording(record.RecordingId, record.Programme.RecordingStartedAt, inputs.Evidence, inputs.Context, actions, sought);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(failure, "Recording {Recording} could not be read for learning; this round learns without it.", record.RecordingId.Wire);

            return null;
        }
    }

    private async Task<LearningCatalog> CatalogAsync(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<CatalogOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<RecordingId, ProgrammeCopy> copies = occurrences.Count is 0
            ? new Dictionary<RecordingId, ProgrammeCopy>()
            : await programmes.ProgrammesAsync([.. occurrences.Select(occurrence => occurrence.RecordingId).Distinct()], cancellationToken);
        ILookup<CatalogEntryId, CatalogOccurrence> stretches = occurrences.ToLookup(occurrence => occurrence.EntryId);
        CatalogSound[] sounds =
        [
            .. entries.Select(entry => new CatalogSound(
                entry.Id,
                entry.Length,
                [.. stretches[entry.Id].Select(occurrence => PlacedOccurrence.Of(occurrence, copies.GetValueOrDefault(occurrence.RecordingId)))])),
        ];

        return new LearningCatalog(sounds, SeriesLinks.SharingSound(await catalog.SoundSharersAsync(cancellationToken)));
    }
}

