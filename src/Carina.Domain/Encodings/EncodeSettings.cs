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
/// long a job may go without headway, and how many attempts it gets.
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

    public bool HoldsAnyRoot => OutputRoots.Count > 0;
}
