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
        double skipThreshold,
        DateTime now,
        Func<CancellationToken, Task<bool>> mayGoOn,
        CancellationToken cancellationToken);
}

/// <summary>
/// Gathers what learning reads and learns from it with <see cref="SegmentLearning"/>, a step at a time. The
/// recordings learned from are those <see cref="LearningSelection"/> chooses among the candidates of
/// <see cref="ITrainingWorklist"/>, read the way detection reads them (<see cref="SegmentDetecting.GatherAsync"/>),
/// with the earlier episodes the version in use detected; only their answers and skip events are read, and an
/// answer not confirmed is read against the detection the version in use made of it. Every catalog entry is handed
/// over with all its stretches, each placed in its programme beside the kind the answer on its recording gives it,
/// when that recording is one learned from. A recording whose data cannot be read is left out and said so. Each
/// step of the learning is its own piece of work on the <see cref="LowestPriorityScheduler"/>, so that whatever
/// else waits there goes between them. Nothing is written here.
/// </summary>
public sealed class SegmentTrainer(
    SegmentDetecting detecting,
    ITrainingWorklist candidates,
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
    public async Task<Trained> TrainAsync(
        SegmentModelVersion active,
        double skipThreshold,
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
            .Run(() => SegmentLearning.Begin(material.Recordings, material.Catalog, active, search, skipThreshold, now), cancellationToken)
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

        RecordingAnswer[] standing = [.. (await answers.ListAsync(chosen, cancellationToken)).Where(answer => answer.Stands)];
        ILookup<RecordingId, ViewerAction> actions = (await skips.ListAsync(chosen, cancellationToken)).ToLookup(skip => skip.RecordingId, skip => skip.Action());
        IReadOnlyDictionary<RecordingId, GivenAnswer> given = await GivenAsync(standing, active, cancellationToken);
        List<LearningRecording> recordings = [];

        foreach (LearningExtraction record in (await worklist.RecordsAsync(chosen, cancellationToken)).Where(record => record.ReadThrough > TimeSpan.Zero && record.Version is not null))
        {
            if (await ReadAsync(record, active, now, given.GetValueOrDefault(record.RecordingId), [.. actions[record.RecordingId]], cancellationToken) is { } read)
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
