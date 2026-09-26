using Carina.Domain.Integrity;

namespace Carina.Domain.Encodings;

/// <summary>
/// How jobs are run on this machine. <see cref="OutputRoots"/> names the roots this process holds
/// for writing and where each is mounted: an artefact is placed only under one of these, never
/// under a root the recordings are read from, which this process holds read-only. Left unset,
/// nothing can be encoded and the check at startup says so. A work file is written beside the
/// artefact it will become, under the same root, so the rename that finishes the job never crosses
/// a mount; set, <see cref="WorkedIn"/> names one directory for every root, and the check at
/// startup refuses a directory on another mount than any root. The rest says
/// whether a recording that has ended is queued without anyone asking, which encoder a job asks
/// for first, how many of the machine's cores a run may use, how often the queue is looked at, how
/// long a job may go without making headway, and how many attempts it gets before it is given up.
/// <see cref="Chapters"/> is the separate matter of looking for the breaks in a recording first.
/// </summary>
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
/// How a run looks for the breaks in a recording before it encodes it. <see cref="Marked"/> is the
/// switch: turned off, nothing looks and a run's command line is what it was before. The rest is
/// what the look is made of — how quiet a stretch has to be to count as quiet and for how long,
/// how much the picture has to change to count as a change, the grid a pod of advertisements is
/// laid on and how far off that grid a pair of boundaries may sit and still be taken as a pair —
/// and two safety valves: <see cref="MostBreakShare"/> throws the whole reading away when it took
/// more than that much of the recording for breaks, and <see cref="MostChapters"/> throws it away
/// when it put in more marks than that. <see cref="Watermark"/> says whether the look also watches
/// the picture for the station's watermark — learning it from this recording for the ones after it,
/// and judging this one by the one learned ahead — which costs one more pass over the picture.
/// Every one of them is a number, a truth or a length of time; none of them is text a filter could
/// be written in.
/// </summary>
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
