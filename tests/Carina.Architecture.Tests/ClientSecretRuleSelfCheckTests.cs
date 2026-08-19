namespace Carina.Architecture.Tests;

public sealed class ClientSecretRuleSelfCheckTests
{
    [Fact]
    public void DetectsASourceThatWouldHandTheClientSecretToALog()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-client-secret-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Quiet.cs"),
                """
                namespace Sample;
                public static class Spend
                {
                    public static string Body(Held held) => held.ClientSecret!.Value;
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Loud.cs"),
                """
                namespace Sample;
                public sealed class Spend(ILogger<Spend> logger)
                {
                    public void Body(Held held) => logger.LogInformation("{Secret}", held.ClientSecret!.Value);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Elsewhere.cs"),
                """
                namespace Sample;
                public sealed class Note(ILogger<Note> logger)
                {
                    public void Body() => logger.LogInformation("nothing secret here");
                }
                """);

            Assert.Equal(
                ["Loud.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    AuthenticationBypasses.ClientSecretInTheClear,
                    AuthenticationBypasses.Logging));
            Assert.Equal(
                ["Loud.cs", "Quiet.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. AuthenticationBypasses.ClientSecretInTheClear]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
