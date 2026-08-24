using Carina.Infrastructure.Persistence.Configurations;

namespace Carina.Db.Tests;

public sealed class RecordingGuardSqlTests
{
    public static TheoryData<string, string> Guards => new()
    {
        { nameof(RecordingGuards.Functions), RecordingGuards.Functions },
        { nameof(RecordingGuards.Projection), RecordingGuards.Projection },
        { nameof(RecordingGuards.Immutability), RecordingGuards.Immutability },
    };

    [Theory]
    [MemberData(nameof(Guards))]
    public void SomeMigrationInstallsWhatTheSchemaCallsFor(string name, string sql)
    {
        string marker = Marker(sql);

        Assert.True(
            Migrations().Any(migration => File.ReadAllText(migration).Contains(marker, StringComparison.Ordinal)),
            $"No migration installs {name}. Add one that runs it.");
    }

    [Theory]
    [MemberData(nameof(Guards))]
    public void TheLastMigrationToInstallItInstallsWhatItSaysToday(string name, string sql)
    {
        string marker = Marker(sql);
        string newest = Migrations()
            .Where(migration => File.ReadAllText(migration).Contains(marker, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Last();

        Assert.True(
            File.ReadAllText(newest).Contains(Embedded(sql), StringComparison.Ordinal),
            $"{Path.GetFileName(newest)} installs an older {name}. "
            + "A migration that has run somewhere is frozen, so add a new migration rather than editing it.");
    }

    [Fact]
    public void TheMigrationRunsThoseThreeAndNothingElse()
    {
        string newest = Migrations()
            .Where(migration => File.ReadAllText(migration).Contains("CREATE OR REPLACE", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Last();

        Assert.Equal(
            [RecordingGuards.Functions, RecordingGuards.Projection, RecordingGuards.Immutability],
            Ran(File.ReadAllText(newest)));
    }

    [Fact]
    public void TheMigrationLeavesTheSystemColumnToPostgres()
    {
        foreach (string migration in Migrations())
        {
            Assert.DoesNotContain(
                $"{RecordingConfiguration.ConcurrencyToken} = table.Column",
                File.ReadAllText(migration),
                StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<string> Ran(string migration)
    {
        string fence = new('"', 3);
        string opening = $"        migrationBuilder.Sql(\n            {fence}\n";
        string closing = $"\n            {fence});";
        var blocks = new List<string>();

        for (int at = 0; (at = migration.IndexOf(opening, at, StringComparison.Ordinal)) >= 0;)
        {
            int from = at + opening.Length;
            int to = migration.IndexOf(closing, from, StringComparison.Ordinal);
            blocks.Add(Dedented(migration[from..to]));
            at = to + closing.Length;
        }

        return blocks;
    }

    private static string Dedented(string block)
        => string.Join(
            '\n',
            block.Split('\n').Select(line => line.StartsWith(new string(' ', 12), StringComparison.Ordinal)
                ? line[12..]
                : line));

    private static string Marker(string sql)
        => sql.Split('\n').First(line => line.StartsWith("CREATE", StringComparison.Ordinal));

    private static string Embedded(string sql)
        => string.Join('\n', sql.Split('\n').Select(line => (new string(' ', 12) + line).TrimEnd()));

    private static IEnumerable<string> Migrations()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Carina.slnx")))
            {
                return Directory
                    .EnumerateFiles(Path.Combine(directory.FullName, "src", "Carina.Db", "Migrations"), "*.cs")
                    .Where(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal))
                    .ToArray();
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
