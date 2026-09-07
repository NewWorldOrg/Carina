namespace Carina.Domain.Migration;

public enum MigrationRefusal
{
    ReallyEmpty = 1,

    FileMissing = 2,

    Orphan = 3,

    Unidentifiable = 4,

    Inexpressible = 5,

    NoSuchFeature = 6,

    OutOfScope = 7,
}

public static class MigrationRefusals
{
    public static readonly IReadOnlyList<MigrationRefusal> All = [.. Enum.GetValues<MigrationRefusal>()];

    public static MigrationRefusal Named(MigrationRefusal refusal)
        => Enum.IsDefined(refusal)
            ? refusal
            : throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "Everything left behind is left behind for a reason the record can name.");
}
