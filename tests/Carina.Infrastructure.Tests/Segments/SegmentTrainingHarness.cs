using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// Learning as a test sets it: what a candidate and the version in use came to on the recordings judged, or
/// nothing to learn from, judged against the version in use as learning judges it; what to do while learning,
/// failing included; and one boundary after it, where learning asks whether it may go on.
/// </summary>
internal sealed class HeldTrainer : ISegmentTrainer
{
    public SegmentEvaluation? Candidate { get; set; }

    public SegmentEvaluation InUse { get; set; } = SegmentTrainingHarness.Evaluated(0.85, 0.9);

    public Exception? Failure { get; set; }

    public Func<Task>? WhileLearning { get; set; }

    public List<(int Active, DateTime Now)> Asked { get; } = [];

    public async Task<Trained> TrainAsync(
        SegmentModelVersion active,
        DateTime now,
        Func<CancellationToken, Task<bool>> mayGoOn,
        CancellationToken cancellationToken)
    {
        Asked.Add((active.Number, now));

        if (WhileLearning is { } meanwhile)
        {
            await meanwhile();
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        if (!await mayGoOn(cancellationToken))
        {
            return Trained.StoppedPartway;
        }

        return Candidate is { } candidate
            ? new Trained(new SegmentLearned(HandMadeModels.First, candidate, InUse, SegmentAdoption.Judge(active, InUse, candidate)), false)
            : Trained.NothingToLearnFrom;
    }
}

internal sealed class KeptLines<T> : ILogger<T>
{
    private readonly Lock gate = new();

    private readonly List<(LogLevel Level, string Line, Exception? Failure)> lines = [];

    public IReadOnlyList<(LogLevel Level, string Line, Exception? Failure)> Lines
    {
        get
        {
            lock (gate)
            {
                return [.. lines];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (gate)
        {
            lines.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

internal sealed class SegmentTrainingHarness : IDisposable
{
    /// <summary>
    /// Half past three in the morning in Japan, half an hour into the day.
    /// </summary>
    public static readonly DateTime Start = new(2026, 11, 1, 18, 30, 0, DateTimeKind.Utc);

    public static readonly EvaluationEvidence Enough = new(12, 148, Start.AddHours(-6));

    private readonly ServiceProvider provider;

    public SegmentTrainingHarness()
    {
        Busyness = new HeldBusyness(() => new Busyness(Recording, false, null));
        provider = new ServiceCollection()
            .AddSingleton<IBusynessReader>(Busyness)
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .AddSingleton<ISegmentModelVersionRepository>(Versions)
            .AddSingleton<ISkipEventRepository>(Skips)
            .AddSingleton<ISegmentLearningRoundRepository>(Rounds)
            .AddSingleton<ILearningMaterialChanges>(Changes)
            .AddSingleton<ISegmentTrainer>(Trainer)
            .BuildServiceProvider();
    }

    public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Start));

    public HeldBusyness Busyness { get; }

    public HeldSegmentModelVersions Versions { get; } = new();

    public HeldSkipEvents Skips { get; } = new();

    public HeldSegmentLearningRounds Rounds { get; } = new();

    public HeldTrainer Trainer { get; } = new();

    public HeldLearningMaterialChanges Changes { get; } = new();

    public KeptLines<SegmentTrainingJob> Log { get; } = new();

    public bool Recording { get; set; }

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public static SegmentEvaluation Evaluated(double recall, double seconds, EvaluationEvidence? evidence = null)
        => new(
            new SegmentMetrics(recall, Math.Round(recall - 0.1, 2), [new KindMetrics(SegmentKind.Break, seconds)], null, 0),
            [],
            evidence ?? Enough);

    public SegmentTrainingJob Job(SegmentTrainingSettings? settings = null)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), settings ?? SegmentTrainingSettings.Default, Clock, Log);

    /// <summary>
    /// Puts version 2 in use <paramref name="daysAgo"/> days ago in the place of the first, as learning would.
    /// </summary>
    public async Task SecondInUseAsync(int daysAgo)
    {
        DateTime adopted = Now.AddDays(-daysAgo);

        Versions.Others.Add(SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Candidate, false, HandMadeModels.First, adopted.AddHours(-1), null, null, null, null));
        await Versions.ChangeAsync(SegmentVersionChange.Adopting(2), adopted, CancellationToken.None);
        Versions.Written.Clear();
    }

    /// <summary>
    /// Viewers' skips on the buttons of <paramref name="version"/> from <paramref name="from"/>, a minute apart,
    /// and how many of them they came back from.
    /// </summary>
    public async Task SkippedAsync(int version, DateTime from, int skips, int returned)
    {
        RecordingId recording = RecordingId.New();

        for (int at = 0; at < skips; at++)
        {
            await Skips.TakeAsync(Skip(recording, ViewerActionKind.Skipped, version, from.AddMinutes(at)), CancellationToken.None);
        }

        for (int at = 0; at < returned; at++)
        {
            await Skips.TakeAsync(Skip(recording, ViewerActionKind.Returned, version, from.AddMinutes(at).AddSeconds(30)), CancellationToken.None);
        }
    }

    public void Dispose() => provider.Dispose();

    private static SkipEvent Skip(RecordingId recording, ViewerActionKind action, int version, DateTime at)
        => SkipEvent.Of(recording, new ViewerAction(at, action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version));
}
