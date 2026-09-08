namespace Carina.Architecture.Tests;

public sealed class MigrationSourceRuleSelfCheckTests
{
    [Fact]
    public void DetectsASourceThatWouldHandTheConnectionToALogOrCompileOneIn()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-migration-source-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Quiet.cs"),
                """
                namespace Sample;
                public static class Spend
                {
                    public static string Open(Held held) => held.ConnectionAsSupplied;
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Loud.cs"),
                """
                namespace Sample;
                public sealed class Spend(ILogger<Spend> logger)
                {
                    public void Open(Held held) => logger.LogInformation("{How}", held.ConnectionAsSupplied);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Baked.cs"),
                """
                namespace Sample;
                public static class Fallback
                {
                    public const string Otherwise = "Server=somewhere;Uid=root;Pwd=secret";
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Elsewhere.cs"),
                """
                namespace Sample;
                public sealed class Note(ILogger<Note> logger)
                {
                    public void Body() => logger.LogInformation("nothing to do with the source");
                }
                """);

            Assert.Equal(
                ["Loud.cs", "Quiet.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. MigrationSourceReach.ConnectionAsSupplied]));
            Assert.Equal(
                ["Loud.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    MigrationSourceReach.ConnectionAsSupplied,
                    AuthenticationBypasses.Logging));
            Assert.Equal(
                ["Baked.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. MigrationSourceReach.AConnectionOfItsOwn]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
