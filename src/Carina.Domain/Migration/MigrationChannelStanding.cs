namespace Carina.Domain.Migration;

public enum MigrationChannelStanding
{
    NameProposed = 1,

    NothingAnswers = 2,

    Inexpressible = 3,
}

public static class MigrationChannelStandings
{
    public static readonly IReadOnlyList<MigrationChannelStanding> All =
        [.. Enum.GetValues<MigrationChannelStanding>()];

    public static MigrationChannelStanding Named(MigrationChannelStanding standing)
        => Enum.IsDefined(standing)
            ? standing
            : throw new ArgumentOutOfRangeException(
                nameof(standing),
                standing,
                "What became of a channel definition is one of the things the record knows to say.");
}
