using Carina.Infrastructure.Migration;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
public sealed class DbEntryPointTests
{
    [Theory]
    [InlineData]
    [InlineData("--frobnicate")]
    [InlineData("--migrate", "extra")]
    [InlineData("--carry")]
    [InlineData("--carry", "--from", "/a")]
    public async Task PrintsUsageAndExitsNonZeroForAnythingItCannotRun(params string[] args)
    {
        var error = new StringWriter();

        int exitCode = await DbEntryPoint.RunAsync(args, error);

        Assert.Equal(DbEntryPoint.UsageExitCode, exitCode);
        Assert.Contains("usage: Carina.Db --migrate", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("--carry --from", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesToCarryFromADirectoryThatIsNotThere()
    {
        string into = Directory.CreateTempSubdirectory("carina-carry-into").FullName;
        var error = new StringWriter();

        try
        {
            int exitCode = await DbEntryPoint.RunAsync(
                ["--carry", "--from", "/no/such/place", "--into", into],
                error);

            Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
            Assert.Contains("/no/such/place", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(into, recursive: true);
        }
    }

    [Fact]
    public async Task RefusesToCarryIntoARootThatIsNotThere()
    {
        string from = Directory.CreateTempSubdirectory("carina-carry-from").FullName;
        var error = new StringWriter();

        try
        {
            int exitCode = await DbEntryPoint.RunAsync(
                ["--carry", "--from", from, "--into", "/no/such/root"],
                error);

            Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
            Assert.Contains("/no/such/root", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(from, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusesToCarryIntoADirectoryThisInstallationDeclaresNoRootFor(bool forReal)
    {
        string from = Directory.CreateTempSubdirectory("carina-carry-from").FullName;
        string into = Directory.CreateTempSubdirectory("carina-carry-into").FullName;
        using var declared = new EnvironmentVariableScope(
            MigrationRootSettings.DeclarationVariable,
            "primary=/srv/somewhere-else");
        var error = new StringWriter();

        try
        {
            string[] args = forReal
                ? ["--carry", "--from", from, "--into", into, "--for-real"]
                : ["--carry", "--from", from, "--into", into];

            int exitCode = await DbEntryPoint.RunAsync(args, error);

            Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
            Assert.Contains(
                MigrationRootSettings.DeclarationVariable,
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Contains(into, error.ToString(), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(into));
        }
        finally
        {
            Directory.Delete(from, recursive: true);
            Directory.Delete(into, recursive: true);
        }
    }

    [Fact]
    public async Task ADirectoryThisInstallationDeclaresARootForIsNotRefusedForWantOfADeclaration()
    {
        using var unreachable = new EnvironmentVariableScope(MigrationSourceSettings.ConnectionVariable, null);
        string from = Directory.CreateTempSubdirectory("carina-carry-from").FullName;
        string into = Directory.CreateTempSubdirectory("carina-carry-into").FullName;
        using EnvironmentVariableScope declared = Declaring(into);
        var error = new StringWriter();

        try
        {
            int exitCode = await DbEntryPoint.RunAsync(["--carry", "--from", from, "--into", into], error);

            Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
            Assert.DoesNotContain(
                MigrationRootSettings.DeclarationVariable,
                error.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(from, recursive: true);
            Directory.Delete(into, recursive: true);
        }
    }

    [Fact]
    public async Task RefusesToCarryWhenNothingSaysHowToReachTheSourceSystem()
    {
        using var scope = new EnvironmentVariableScope(MigrationSourceSettings.ConnectionVariable, null);
        string from = Directory.CreateTempSubdirectory("carina-carry-from").FullName;
        string into = Directory.CreateTempSubdirectory("carina-carry-into").FullName;
        using EnvironmentVariableScope declared = Declaring(into);
        var error = new StringWriter();

        try
        {
            int exitCode = await DbEntryPoint.RunAsync(["--carry", "--from", from, "--into", into], error);

            Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
            Assert.Contains(
                MigrationSourceSettings.ConnectionVariable,
                error.ToString(),
                StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(into));
        }
        finally
        {
            Directory.Delete(from, recursive: true);
            Directory.Delete(into, recursive: true);
        }
    }

    [Fact]
    public async Task RefusesToCarryWhenWhatSaysHowToReachTheSourceSystemNamesNoDatabase()
    {
        using var scope = new EnvironmentVariableScope(
            MigrationSourceSettings.ConnectionVariable,
            "Server=somewhere.invalid;Uid=reader;Pwd=notreal");
        string from = Directory.CreateTempSubdirectory("carina-carry-from").FullName;
        string into = Directory.CreateTempSubdirectory("carina-carry-into").FullName;
        using EnvironmentVariableScope declared = Declaring(into);
        var error = new StringWriter();

        try
        {
            int exitCode = await DbEntryPoint.RunAsync(["--carry", "--from", from, "--into", into], error);

            Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
            Assert.Contains("names no database", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(from, recursive: true);
            Directory.Delete(into, recursive: true);
        }
    }

    [Fact]
    public async Task FailsLoudlyWhenTheConnectionStringVariableIsMissing()
    {
        using var scope = new EnvironmentVariableScope(CarinaDbContextFactory.ConnectionStringVariable, null);
        var error = new StringWriter();

        int exitCode = await DbEntryPoint.RunAsync(["--migrate"], error);

        Assert.Equal(DbEntryPoint.UnusableConfigurationExitCode, exitCode);
        Assert.Contains(CarinaDbContextFactory.ConnectionStringVariable, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsLoudlyOnAMalformedConnectionString()
    {
        using var scope = new EnvironmentVariableScope(
            CarinaDbContextFactory.ConnectionStringVariable,
            "this is not a connection string");
        var error = new StringWriter();

        int exitCode = await DbEntryPoint.RunAsync(["--migrate"], error);

        Assert.Equal(DbEntryPoint.MigrationFailedExitCode, exitCode);
        Assert.Contains("Carina.Db --migrate failed", error.ToString(), StringComparison.Ordinal);
    }

    private static EnvironmentVariableScope Declaring(string into)
        => new(MigrationRootSettings.DeclarationVariable, $"primary={into}");
}
