using Carina.Infrastructure.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationSourceSettingsTests
{
    private const string Complete = "Server=somewhere.invalid;Port=3306;Database=somedb;Uid=reader;Pwd=notreal";

    [Fact]
    public void WithNothingSuppliedTheRunWillNotHappenRatherThanFallBackToSomething()
    {
        Assert.False(MigrationSourceSettings.TryRead(
            Nothing,
            out MigrationSourceSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains(MigrationSourceSettings.ConnectionVariable, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptySettingIsTheSameAsNoSettingAtAll()
    {
        Assert.False(MigrationSourceSettings.TryRead(
            Supplying("   "),
            out MigrationSourceSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void SomethingThatIsNotAConnectionAtAllIsRefusedWithoutRepeatingIt()
    {
        Assert.False(MigrationSourceSettings.TryRead(
            Supplying("nonsense nobody could parse"),
            out MigrationSourceSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.DoesNotContain("nonsense", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionThatNamesNoHostIsRefusedRatherThanAimedAtThisMachine()
    {
        Assert.False(MigrationSourceSettings.TryRead(
            Supplying("Database=somedb;Uid=reader;Pwd=notreal"),
            out MigrationSourceSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains("host", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionThatNamesNoDatabaseIsRefused()
    {
        Assert.False(MigrationSourceSettings.TryRead(
            Supplying("Server=somewhere.invalid;Uid=reader;Pwd=notreal"),
            out MigrationSourceSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains("database", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionThatNamesNoAccountIsRefused()
    {
        Assert.False(MigrationSourceSettings.TryRead(
            Supplying("Server=somewhere.invalid;Database=somedb"),
            out MigrationSourceSettings? read,
            out string problem));

        Assert.Null(read);
        Assert.Contains("account", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionNamingHostDatabaseAndAccountIsAccepted()
    {
        Assert.True(MigrationSourceSettings.TryRead(
            Supplying(Complete),
            out MigrationSourceSettings? read,
            out string problem));

        Assert.NotNull(read);
        Assert.Empty(problem);
    }

    [Fact]
    public void OnlyTheOneVariableIsEverAskedFor()
    {
        List<string> asked = [];

        MigrationSourceSettings.TryRead(
            name =>
            {
                asked.Add(name);

                return name == MigrationSourceSettings.ConnectionVariable ? Complete : "somewhere else";
            },
            out MigrationSourceSettings? read,
            out _);

        Assert.NotNull(read);
        Assert.Equal([MigrationSourceSettings.ConnectionVariable], asked);
    }

    [Fact]
    public void SayingWhatItIsNeverSaysWhatItHolds()
    {
        MigrationSourceSettings.TryRead(Supplying(Complete), out MigrationSourceSettings? read, out _);

        Assert.NotNull(read);
        Assert.DoesNotContain("notreal", read.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("somewhere.invalid", read.ToString(), StringComparison.Ordinal);
        Assert.Contains(MigrationSourceSettings.ConnectionVariable, read.ToString(), StringComparison.Ordinal);
    }

    private static Func<string, string?> Supplying(string value)
        => name => name == MigrationSourceSettings.ConnectionVariable ? value : null;

    private static string? Nothing(string name) => null;
}
