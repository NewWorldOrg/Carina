using Carina.Infrastructure.Persistence.Configurations;

namespace Carina.Db.Tests;

public sealed class RecordingGuardSqlTests
{
    [Fact]
    public void TheMigrationInstallsExactlyTheFunctionsTheConstraintsCall()
    {
        string migration = File.ReadAllText(Path.Combine(
            Migrations(),
            "20260824013352_Recordings.cs"));

        string installed = string.Join(
            '\n',
            RecordingGuards.Functions.Split('\n').Select(line => (new string(' ', 12) + line).TrimEnd()));

        Assert.Contains(installed, migration, StringComparison.Ordinal);
    }

    private static string Migrations()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Carina.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "Carina.Db", "Migrations");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
