using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What a round of learning learns from: every recording read and given something to learn it by, and the
/// catalog as learning reads it.
/// </summary>
public sealed record TrainingMaterial(IReadOnlyList<LearningRecording> Recordings, LearningCatalog Catalog);

/// <summary>
/// What learning came to: the candidate learned; or nothing, when there was nothing to learn from or learning
/// stopped partway, what was done thrown away.
/// </summary>
public sealed record Trained(SegmentLearned? Learned, bool Stopped)
{
    public static Trained NothingToLearnFrom { get; } = new(null, false);

    public static Trained StoppedPartway { get; } = new(null, true);
}

/// <summary>
/// Learns a candidate beside the version in use.
/// </summary>
public interface ISegmentTrainer
{
    /// <summary>
    /// What learning against <paramref name="active"/> at <paramref name="now"/> came to. Learning asks
    /// <paramref name="mayGoOn"/> after each recording it reads and after each step it takes, and stops the
    /// first time it is told no.
    /// </summary>
    Task<Trained> TrainAsync(
        SegmentModelVersion active,
        DateTime now,
        Func<CancellationToken, Task<bool>> mayGoOn,
        CancellationToken cancellationToken);
}

/// <summary>
/// Gathers what learning reads and learns from it with <see cref="SegmentLearning"/>, a step at a time. The
/// recordings learned from are those <see cref="LearningSelection"/> chooses among the candidates of
/// <see cref="ITrainingWorklist"/>, read the way detection reads them (<see cref="SegmentDetecting.GatherAsync"/>),
/// with the earlier episodes as the version in use detected them; only their skip events and seeks are read. Every catalog
/// entry is handed over with all its stretches, each placed in its programme. A recording whose data cannot be
/// read is left out and said so. Each step of the learning is its own piece of work on the
/// <see cref="LowestPriorityScheduler"/>, so that whatever else waits there goes between them. Nothing is written
/// here.
/// </summary>
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

    /// <summary>
    /// What a round learns from; none when <paramref name="mayGoOn"/> said no after a recording was read.
    /// </summary>
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

        foreach (LearningExtraction record in (await worklist.RecordsAsync(chosen, cancellationToken)).Where(record => record.ReadThrough > TimeSpan.Zero && record.Version is not null))
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

