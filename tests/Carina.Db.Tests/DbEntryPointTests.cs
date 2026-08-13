namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
public sealed class DbEntryPointTests
{
    [Theory]
    [InlineData]
    [InlineData("--frobnicate")]
    [InlineData("--migrate", "extra")]
    public async Task PrintsUsageAndExitsNonZeroForAnythingButMigrate(params string[] args)
    {
        var error = new StringWriter();

        var exitCode = await DbEntryPoint.RunAsync(args, error);

        Assert.Equal(DbEntryPoint.UsageExitCode, exitCode);
        Assert.Contains("usage: Carina.Db --migrate", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsLoudlyWhenTheConnectionStringVariableIsMissing()
    {
        using var scope = new EnvironmentVariableScope(CarinaDbContextFactory.ConnectionStringVariable, null);
        var error = new StringWriter();

        var exitCode = await DbEntryPoint.RunAsync(["--migrate"], error);

        Assert.Equal(DbEntryPoint.MigrationFailedExitCode, exitCode);
        Assert.Contains("Carina.Db --migrate failed", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(CarinaDbContextFactory.ConnectionStringVariable, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsLoudlyOnAMalformedConnectionString()
    {
        using var scope = new EnvironmentVariableScope(
            CarinaDbContextFactory.ConnectionStringVariable,
            "this is not a connection string");
        var error = new StringWriter();

        var exitCode = await DbEntryPoint.RunAsync(["--migrate"], error);

        Assert.Equal(DbEntryPoint.MigrationFailedExitCode, exitCode);
        Assert.Contains("Carina.Db --migrate failed", error.ToString(), StringComparison.Ordinal);
    }
}
