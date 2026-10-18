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
/// Learns a candidate beside the version in use.
/// </summary>
public interface ISegmentTrainer
{
    /// <summary>
    /// What learning against <paramref name="active"/> at <paramref name="now"/> made; none when there was
    /// nothing to learn from.
    /// </summary>
    Task<SegmentLearned?> TrainAsync(SegmentModelVersion active, double skipThreshold, DateTime now, CancellationToken cancellationToken);
}

/// <summary>
/// Gathers what learning reads and learns from it with <see cref="SegmentLearning"/>. The recordings learned
/// from are those whose learning data was read to its end or as far as it could be, whether or not the
/// recording is still kept, that have an answer standing, a stretch the catalog heard in them or a skip event;
/// each is read the way detection reads it (<see cref="SegmentDetecting.GatherAsync"/>), with the earlier
/// episodes the version in use detected, and an answer not confirmed is read against the detection the version
/// in use made of it. Every catalog entry is handed over with all its stretches, each placed in its programme
/// beside the kind the answer on its recording gives it. A recording whose data cannot be read is left out and
/// said so. The learning runs on the <see cref="LowestPriorityScheduler"/>. Nothing is written here.
/// </summary>
public sealed class SegmentTrainer(
    SegmentDetecting detecting,
    IDetectionWorklist worklist,
    ICatalogRepository catalog,
    ICatalogWorklist programmes,
    ISegmentAnswerRepository answers,
    ISkipEventRepository skips,
    IRecordingDetectionRepository detections,
    LowestPriorityScheduler lowest,
    SegmentTrainingSettings settings,
    ILogger<SegmentTrainer> logger) : ISegmentTrainer
{
    public async Task<SegmentLearned?> TrainAsync(SegmentModelVersion active, double skipThreshold, DateTime now, CancellationToken cancellationToken)
    {
        TrainingMaterial material = await GatherAsync(active, now, cancellationToken);

        if (material.Recordings.Count is 0)
        {
            return null;
        }

        SegmentSearch search = settings.Search;

        return await lowest
            .Run(() => SegmentLearning.Learn(material.Recordings, material.Catalog, active, search, skipThreshold, now), cancellationToken)
            .WaitAsync(cancellationToken);
    }

    public async Task<TrainingMaterial> GatherAsync(SegmentModelVersion active, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);

        RecordingAnswer[] standing = [.. (await answers.ListAsync(cancellationToken)).Where(answer => answer.Stands)];
        ILookup<RecordingId, ViewerAction> actions = (await skips.ListAsync(cancellationToken)).ToLookup(skip => skip.RecordingId, skip => skip.Action());
        IReadOnlyList<CatalogEntry> entries = await catalog.ListEntriesAsync(cancellationToken);
        IReadOnlyList<CatalogOccurrence> occurrences = entries.Count is 0
            ? []
            : await catalog.OccurrencesOfAsync([.. entries.Select(entry => entry.Id)], cancellationToken);
        IReadOnlyDictionary<RecordingId, GivenAnswer> given = await GivenAsync(standing, active, cancellationToken);
        RecordingId[] labelled = [.. given.Keys.Concat(actions.Select(group => group.Key)).Concat(occurrences.Select(occurrence => occurrence.RecordingId)).Distinct()];
        IReadOnlyList<LearningExtraction> records = labelled.Length is 0 ? [] : await worklist.RecordsAsync(labelled, cancellationToken);
        List<LearningRecording> recordings = [];

        foreach (LearningExtraction record in records.Where(record => record.ReadThrough > TimeSpan.Zero && record.Version is not null))
        {
            if (await ReadAsync(record, active, now, given.GetValueOrDefault(record.RecordingId), [.. actions[record.RecordingId]], cancellationToken) is { } read)
            {
                recordings.Add(read);
            }
        }

        return new TrainingMaterial(recordings, await CatalogAsync(entries, occurrences, given, cancellationToken));
    }

    private async Task<IReadOnlyDictionary<RecordingId, GivenAnswer>> GivenAsync(RecordingAnswer[] standing, SegmentModelVersion active, CancellationToken cancellationToken)
    {
        if (standing.Length is 0)
        {
            return new Dictionary<RecordingId, GivenAnswer>();
        }

        Dictionary<RecordingId, RecordingDetection> detected = (await detections.ListAsync([.. standing.Select(answer => answer.RecordingId)], active.Number, cancellationToken))
            .ToDictionary(detection => detection.RecordingId);

        return standing.ToDictionary(
            answer => answer.RecordingId,
            answer => new GivenAnswer(answer.Segments, answer.Confirmed, answer.AnsweredAt, false, Shown(detected.GetValueOrDefault(answer.RecordingId))));
    }

    private async Task<LearningRecording?> ReadAsync(
        LearningExtraction record,
        SegmentModelVersion active,
        DateTime now,
        GivenAnswer? answer,
        IReadOnlyList<ViewerAction> actions,
        CancellationToken cancellationToken)
    {
        try
        {
            DetectionInputs inputs = await detecting.GatherAsync(record, active.Number, now, cancellationToken);

            return new LearningRecording(record.RecordingId, record.Programme.RecordingStartedAt, inputs.Evidence, inputs.Context, answer, actions);
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
        IReadOnlyDictionary<RecordingId, GivenAnswer> given,
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
                [.. stretches[entry.Id].Select(occurrence => Placed(occurrence, copies, given))])),
        ];

        return new LearningCatalog(sounds, SeriesLinks.SharingSound(await catalog.FixtureSharersAsync(cancellationToken)));
    }

    private static PlacedOccurrence Placed(
        CatalogOccurrence occurrence,
        IReadOnlyDictionary<RecordingId, ProgrammeCopy> copies,
        IReadOnlyDictionary<RecordingId, GivenAnswer> given)
        => PlacedOccurrence.Of(
            occurrence,
            copies.GetValueOrDefault(occurrence.RecordingId),
            SegmentLabels.AnswerOver(occurrence.StartsAt, occurrence.EndsAt, given.GetValueOrDefault(occurrence.RecordingId)));

    /// <summary>
    /// The stretches a detection showed the person who answered; none of one thrown away.
    /// </summary>
    private static IReadOnlyList<SegmentSpan>? Shown(RecordingDetection? detection)
        => detection switch
        {
            null => null,
            { State: SegmentDetectionState.Discarded } => [],
            _ => detection.Segments,
        };
}
