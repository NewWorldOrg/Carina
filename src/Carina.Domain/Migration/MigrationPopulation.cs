namespace Carina.Domain.Migration;

public enum MigrationPopulation
{
    Recordings = 1,

    RecordingFiles = 2,

    Rules = 3,

    Reservations = 4,

    ChannelDefinitions = 5,

    ProgrammeGuide = 6,
}

public static class MigrationPopulations
{
    public static readonly IReadOnlyList<MigrationPopulation> All =
    [
        MigrationPopulation.Recordings,
        MigrationPopulation.RecordingFiles,
        MigrationPopulation.Rules,
        MigrationPopulation.Reservations,
        MigrationPopulation.ChannelDefinitions,
        MigrationPopulation.ProgrammeGuide,
    ];

    public static readonly IReadOnlyList<MigrationPopulation> Counted =
    [
        MigrationPopulation.Recordings,
        MigrationPopulation.RecordingFiles,
        MigrationPopulation.Rules,
        MigrationPopulation.Reservations,
        MigrationPopulation.ChannelDefinitions,
    ];

    public static MigrationPopulation Named(MigrationPopulation population)
        => Enum.IsDefined(population)
            ? population
            : throw new ArgumentOutOfRangeException(
                nameof(population),
                population,
                "A population is one the migration can name.");

    public static MigrationPopulation Countable(MigrationPopulation population)
        => Counted.Contains(Named(population))
            ? population
            : throw new ArgumentOutOfRangeException(
                nameof(population),
                population,
                "The programme guide is recorded as something not done, never as rows of its own.");
}
