namespace Carina.Architecture.Tests;

public sealed class PlaybackTicketRuleTests
{
    [Fact]
    public void APlaybackTicketIsHandledInTheseFilesAndNowhereElse()
    {
        Assert.Equal(
            [
                "Carina.Api/Authentication/DefaultDenyAuthenticationMiddleware.cs",
                "Carina.Api/Authentication/PlaybackTicketCarrier.cs",
                "Carina.Api/Authentication/PlaybackTicketGate.cs",
                "Carina.Api/Controllers/Live/IssueLiveTicketAction.cs",
                "Carina.Api/Controllers/Videos/IssueVideoTicketAction.cs",
                "Carina.Api/Extensions/ServiceCollectionExtensions.cs",
                "Carina.Api/Playback/VideoDelivery.cs",
                "Carina.Api/Responder/Playback/PlaybackTicketResponder.cs",
                "Carina.Api/Services/AuthSessionService.cs",
                "Carina.Api/Services/LiveService.cs",
                "Carina.Api/Services/PlaybackTicketService.cs",
                "Carina.Domain/Auth/IPlaybackGrantStore.cs",
                "Carina.Domain/Auth/IPlaybackTicketStore.cs",
                "Carina.Domain/Auth/PlaybackGrant.cs",
                "Carina.Domain/Auth/PlaybackTicket.cs",
                "Carina.Domain/Auth/PlaybackTicketPolicy.cs",
                "Carina.Infrastructure/Auth/PlaybackGrantStore.cs",
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

    [Fact]
    public void TheTicketTravelsOnTheAuthorizationHeaderAndOnlyOneFileReadsIt()
    {
        Assert.Equal(
            ["Carina.Api/Authentication/PlaybackTicketCarrier.cs"],
            SourceScan.FilesMentioning(
                RepositoryLayout.SourceDirectory,
                [.. AuthenticationBypasses.ReadingTheAuthorizationHeader]));
    }

    [Fact]
    public void NothingAnywhereTurnsOnTheLoggingThatWouldWriteDownWholeRequestHeaders()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            "AddHttpLogging",
            "UseHttpLogging",
            "UseDeveloperExceptionPage"));
    }
}
