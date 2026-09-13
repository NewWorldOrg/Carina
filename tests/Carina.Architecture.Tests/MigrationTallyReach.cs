namespace Carina.Architecture.Tests;

public static class MigrationTallyReach
{
    public static IReadOnlyList<string> WhatARunCounted { get; } =
    [
        "MigrationTally",
        "MigrationDetail",
        "MigrationLoss",
        "MigrationStanding",
        "MigrationRoll",
        "MigrationVerdict",
    ];

    public static IReadOnlyList<string> WhereACountMayBeRead { get; } =
    [
        "Carina.Api/Controllers/Migration/",
        "Carina.Api/Responder/Migration/",
        "Carina.Api/Services/MigrationRecordService.cs",
        "Carina.Db/CarrySaid.cs",
        "Carina.Db/Migrations/",
        "Carina.Db/DbEntryPoint.cs",
        "Carina.Domain/Migration/",
        "Carina.Infrastructure/Migration/",
        "Carina.Infrastructure/Persistence/Configurations/Migration",
    ];

    public static bool ReadsOutsideTheRecord(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return !WhereACountMayBeRead.Any(place => file.StartsWith(place, StringComparison.Ordinal));
    }
}
