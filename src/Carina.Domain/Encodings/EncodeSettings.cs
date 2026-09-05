using Carina.Domain.Integrity;

namespace Carina.Domain.Encodings;

/// <summary>
/// How jobs are run on this machine. <see cref="OutputRoots"/> names the roots this process holds
/// for writing and where each is mounted: an artefact is placed only under one of these, never
/// under a root the recordings are read from, which this process holds read-only. Left unset,
/// nothing can be encoded and the check at startup says so. A work file is written beside the
/// artefact it will become, under the same root, so the rename that finishes the job never crosses
/// a mount; set, <see cref="WorkedIn"/> names one directory for every root, and the check at
/// startup refuses a directory on another mount than any root (A-エンコード-024). The rest says
/// which encoder a job asks for first, how many of the machine's cores a run may use, how often
/// the queue is looked at, how long a job may go without making headway, and how many attempts it
/// gets before it is given up.
/// </summary>
public sealed record EncodeSettings
{
    public IReadOnlyList<StorageRootPath> OutputRoots { get; init; } = [];

    public string? WorkedIn { get; init; }

    public EncodeEncoder Prefer { get; init; } = EncodeEncoder.Software;

    public int MostCores { get; init; } = 2;

    public int MostAttempts { get; init; } = 3;

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan StalledAfter { get; init; } = TimeSpan.FromMinutes(10);

    public bool HoldsAnyRoot => OutputRoots.Count > 0;
}
