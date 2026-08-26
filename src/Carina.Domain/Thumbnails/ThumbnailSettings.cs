namespace Carina.Domain.Thumbnails;

public sealed record ThumbnailSettings
{
    public TimeSpan BeforeFirstPass { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan BetweenPasses { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan NoLaterThan { get; init; } = TimeSpan.FromSeconds(120);

    public int OneOverAShareOf { get; init; } = 3;

    public TimeSpan LongestRender { get; init; } = TimeSpan.FromSeconds(30);

    public int AtMostAPass { get; init; } = 8;

    public int Width { get; init; } = 960;

    public string Programme { get; init; } = "ffmpeg";

    public string? WrittenTo { get; init; }

    public bool DrawsAnything => WrittenTo is not null;

    public TimeSpan PositionIn(TimeSpan written)
    {
        if (written < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(written),
                written,
                "A recording is not shorter than nothing.");
        }

        var share = new TimeSpan(written.Ticks / OneOverAShareOf);

        return share < NoLaterThan ? share : NoLaterThan;
    }
}
