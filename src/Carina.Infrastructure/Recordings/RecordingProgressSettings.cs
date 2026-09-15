namespace Carina.Infrastructure.Recordings;

/// <summary>
/// How often the screens are told that a recording in progress has moved on when all that moved is
/// what it counts: the time written, the packets lost or left scrambled, and where the losses fell.
/// A recording that starts, stops, breaks or ends is told at once and is not paced by this.
/// </summary>
public sealed record RecordingProgressSettings
{
    public static readonly TimeSpan LongestPace = TimeSpan.FromHours(1);

    public static readonly RecordingProgressSettings Default = new(TimeSpan.FromSeconds(30));

    public RecordingProgressSettings(TimeSpan atMostEvery)
    {
        if (atMostEvery <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(atMostEvery),
                atMostEvery,
                "Counts told with no gap between them are told on every pass, which is what the pace is there to stop.");
        }

        if (atMostEvery > LongestPace)
        {
            throw new ArgumentOutOfRangeException(
                nameof(atMostEvery),
                atMostEvery,
                $"Counts told less often than every {LongestPace} no longer move on a screen while the programme is on.");
        }

        AtMostEvery = atMostEvery;
    }

    public TimeSpan AtMostEvery { get; }
}
