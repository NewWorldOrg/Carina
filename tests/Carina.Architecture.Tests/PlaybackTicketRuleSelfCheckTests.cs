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
                Path.Combine(directory.FullName, "Shouting.cs"),
                """
                namespace Sample;
                public static class Spend
                {
                    public static void Offered(IssuedPlaybackTicket issued)
                        => Console.Error.WriteLine(issued.InTheClear);
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
                ["Loud.cs", "Shouting.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    AuthenticationBypasses.PlaybackTicketHandling,
                    AuthenticationBypasses.Logging));
            Assert.Equal(
                ["Loud.cs", "Quiet.cs", "Shouting.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. AuthenticationBypasses.PlaybackTicketHandling]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact(DisplayName = "detects a source that would write the path a request came on to a log")]
    public void DetectsASourceThatWouldWriteTheRequestPathToALog()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-request-path-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Loud.cs"),
                """
                namespace Sample;
                public sealed class Fell(ILogger<Fell> logger)
                {
                    public void Said(HttpContext context) => logger.LogError("{Path}", context.Request.Path.Value);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Url.cs"),
                """
                namespace Sample;
                public static class Fell
                {
                    public static void Said(HttpContext context) => Console.Error.WriteLine(context.Request.GetDisplayUrl());
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Held.cs"),
                """
                namespace Sample;
                public sealed class Fell(ILogger<Fell> logger)
                {
                    public void Said(HttpRequest request) => logger.LogWarning("{Path}", request.Path);
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Quiet.cs"),
                """
                namespace Sample;
                public static class Matched
                {
                    public static bool Api(HttpContext context) => context.Request.Path.StartsWithSegments("/api");
                }
                """);

            Assert.Equal(
                ["Held.cs", "Loud.cs", "Url.cs"],
                SourceScan.FilesMentioningBoth(
                    directory.FullName,
                    AuthenticationBypasses.ReadingTheRequestPath,
                    AuthenticationBypasses.Logging));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DetectsASecondReaderOfTheAuthorizationHeader()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-authorization-header-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Reader.cs"),
                """
                namespace Sample;
                public static class Peek
                {
                    public static string? Offered(HttpRequest request) => request.Headers.Authorization;
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Elsewhere.cs"),
                """
                namespace Sample;
                public static class Peek
                {
                    public static string? Agent(HttpRequest request) => request.Headers.UserAgent;
                }
                """);

            Assert.Equal(
                ["Reader.cs"],
                SourceScan.FilesMentioning(
                    directory.FullName,
                    [.. AuthenticationBypasses.ReadingTheAuthorizationHeader]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheseRulesReadSourceTextAndAReaderSpelledAnotherWayWalksStraightPast()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-rule-limits-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sideways.cs"),
                """
                namespace Sample;
                public static class Peek
                {
                    private const string Header = "Author" + "ization";

                    public static string? Offered(HttpRequest request) => request.Headers[Header];
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Ordinary.cs"),
                """
                namespace Sample;
                public static class Peek
                {
                    public static string? Offered(HttpRequest request)
                        => request.Headers.TryGetValue("Authorization", out StringValues offered)
                            ? offered.ToString()
                            : null;
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Renamed.cs"),
                """
                namespace Sample;
                public sealed class Pass(ILogger<Pass> logger)
                {
                    public void Offered(string admission) => logger.LogInformation("{Pass}", admission);
                }
                """);

            Assert.Empty(SourceScan.FilesMentioning(
                directory.FullName,
                [.. AuthenticationBypasses.ReadingTheAuthorizationHeader]));
            Assert.Empty(SourceScan.FilesMentioningBoth(
                directory.FullName,
                AuthenticationBypasses.PlaybackTicketHandling,
                AuthenticationBypasses.Logging));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
