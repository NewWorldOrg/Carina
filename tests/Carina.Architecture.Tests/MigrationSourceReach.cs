namespace Carina.Architecture.Tests;

public static class MigrationSourceReach
{
    public static IReadOnlyList<string> ConnectionAsSupplied { get; } =
    [
        "ConnectionAsSupplied",
    ];

    public static IReadOnlyList<string> TheSetting { get; } =
    [
        "CARINA_MIGRATION_SOURCE_CONNECTION",
    ];

    public static IReadOnlyList<string> AConnectionOfItsOwn { get; } =
    [
        "Server=",
        "Host=",
        "Uid=",
        "Pwd=",
        "User ID=",
        "Password=",
    ];
}
