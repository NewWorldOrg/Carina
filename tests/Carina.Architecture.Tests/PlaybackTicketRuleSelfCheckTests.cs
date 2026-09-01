namespace Carina.Architecture.Tests;

public sealed class PlaybackTicketRuleSelfCheckTests
{
    [Fact]
    public void DetectsASourceThatWouldHandAPlaybackTicketToALog()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-playback-ticket-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Quiet.cs"),
                """
                namespace Sample;
                public static class Spend
                {
                    public static string Offered(IssuedPlaybackTicket issued) => issued.InTheClear;
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Loud.cs"),
                """
                namespace Sample;
                public sealed class Spend(ILogger<Spend> logger)
                {
                    public void Offered(IssuedPlaybackTicket issued)
                        => logger.LogInformation("{Ticket}", issued.InTheClear);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Elsewhere.cs"),
                """
                namespace Sample;
                public sealed class Note(ILogger<Note> logger)
                {
                    public void Body() => logger.LogInformation("nothing to hide here");
                }
                """);

            Assert.Equal(
                ["Loud.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    AuthenticationBypasses.PlaybackTicketHandling,
                    AuthenticationBypasses.Logging));
            Assert.Equal(
                ["Loud.cs", "Quiet.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. AuthenticationBypasses.PlaybackTicketHandling]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
