using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public sealed record CaptionSettings
{
    public const string Extension = ".captions";

    public const int TriesAtMost = 3;

    public TimeSpan BeforeFirstPass { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan BetweenPasses { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan LongestTranscription { get; init; } = TimeSpan.FromHours(1);

    public int AtMostAPass { get; init; } = 4;

    public string? WrittenTo { get; init; }

    public bool KeepsAnything => WrittenTo is not null;

    public string? PathOf(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return WrittenTo is { } shelf ? Path.Combine(shelf, id.Wire + Extension) : null;
    }
}
