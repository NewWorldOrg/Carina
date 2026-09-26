using Carina.Domain.Integrity;

namespace Carina.Domain.Encodings;

/// <summary>
/// How jobs are run on this machine.
/// </summary>
/// <remarks>
/// <see cref="OutputRoots"/> names the roots this process holds for writing and where each is
/// mounted; an artefact is placed only under one of these. Left unset, nothing can be encoded and
/// the check at startup says so. A work file is written beside the artefact it will become, unless
/// <see cref="WorkedIn"/> names one directory for every root, which must be on the same mount as a
/// root. The rest says whether a recording that has ended is queued without anyone asking, which
/// encoder a job asks for first, how many cores a run may use, how often the queue is looked at, how
/// long a job may go without headway, and how many attempts it gets. <see cref="Chapters"/> says how
/// the breaks in a recording are looked for first.
/// </remarks>
public sealed record EncodeSettings
{
    public IReadOnlyList<StorageRootPath> OutputRoots { get; init; } = [];

    public string? WorkedIn { get; init; }

    public bool Automatically { get; init; } = true;

    public EncodeEncoder Prefer { get; init; } = EncodeEncoder.Software;

    public int MostCores { get; init; } = 2;

    public int MostAttempts { get; init; } = 3;

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan StalledAfter { get; init; } = TimeSpan.FromMinutes(10);

    public ChapterSettings Chapters { get; init; } = new();

    public bool HoldsAnyRoot => OutputRoots.Count > 0;
}

/// <summary>
/// How a run looks for the breaks in a recording before it encodes it.
/// </summary>
/// <remarks>
/// <see cref="Marked"/> turns the look on. The rest sets how quiet a stretch has to be and for how
/// long, how much the picture has to change, the grid a pod of advertisements is laid on and how far
/// off it a pair of boundaries may sit. <see cref="MostBreakShare"/> and <see cref="MostChapters"/>
/// throw the whole reading away when it took more of the recording for breaks, or put in more marks,
/// than that. <see cref="Watermark"/> also watches the picture for the station's watermark, learning
/// it from this recording and judging this one by the one learned ahead, at the cost of one more
/// pass over the picture.
/// </remarks>
public sealed record ChapterSettings
{
    public bool Marked { get; init; } = true;

    public bool Watermark { get; init; } = true;

    public int Noise { get; init; } = -50;

    public TimeSpan ShortestSilence { get; init; } = TimeSpan.FromMilliseconds(150);

    public double Scene { get; init; } = 0.30;

    public TimeSpan Grid { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan GridTolerance { get; init; } = TimeSpan.FromSeconds(1);

    public double MostBreakShare { get; init; } = 0.5;

    public int MostChapters { get; init; } = 40;
}
