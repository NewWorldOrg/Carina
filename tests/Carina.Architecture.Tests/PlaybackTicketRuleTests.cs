namespace Carina.Architecture.Tests;

public sealed class PlaybackTicketRuleTests
{
    [Fact]
    public void APlaybackTicketIsHandledInTheseFilesAndNowhereElse()
    {
        Assert.Equal(
            [
                "Carina.Api/Authentication/PlaybackTicketCarrier.cs",
                "Carina.Api/Authentication/PlaybackTicketGate.cs",
                "Carina.Api/Extensions/ServiceCollectionExtensions.cs",
                "Carina.Domain/Auth/IPlaybackTicketStore.cs",
                "Carina.Domain/Auth/PlaybackTicket.cs",
                "Carina.Domain/Auth/PlaybackTicketPolicy.cs",
                "Carina.Infrastructure/Auth/PlaybackTicketStore.cs",
                "Carina.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs",
            ],
            SourceScan.FilesMentioning(
                RepositoryLayout.SourceDirectory,
                [.. AuthenticationBypasses.PlaybackTicketHandling]));
    }

    [Fact]
    public void NothingThatHandlesAPlaybackTicketAlsoWritesToALog()
    {
        Assert.Empty(SourceScan.FilesMentioningBoth(
            RepositoryLayout.SourceDirectory,
            AuthenticationBypasses.PlaybackTicketHandling,
            AuthenticationBypasses.Logging));
    }
}
