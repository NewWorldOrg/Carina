namespace Carina.Architecture.Tests;

public sealed class EventStreamRuleSelfCheckTests
{
    [Fact]
    public void DetectsAnEventStreamThatWritesAPayloadFieldAndLeavesSignalsAlone()
    {
        var directory = Directory.CreateTempSubdirectory("carina-event-stream-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Signals.cs"),
                """
                namespace Sample;
                public static class Hub
                {
                    public const string ContentType = "text/event-stream";
                    public static string Frame(string name) => $"event: {name}\n\n";
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Leaky.cs"),
                """
                namespace Sample;
                public static class LeakyHub
                {
                    public const string ContentType = "text/event-stream";
                    public static string Frame(string name, string json) => $"event: {name}\ndata: {json}\n\n";
                }
                """);
            File.WriteAllText(
                Path.Combine(directory.FullName, "Unrelated.cs"),
                """
                namespace Sample;
                public static class Uri
                {
                    public const string Inline = "data:image/png;base64,";
                }
                """);

            Assert.Equal(
                ["Leaky.cs"],
                SourceScan.FilesMentioningAll(
                    directory.FullName,
                    EventStreamContracts.ContentType,
                    EventStreamContracts.PayloadField));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
