using Carina.Domain.Integrity;

namespace Carina.Domain.Encodings;

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
