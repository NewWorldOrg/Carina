namespace Carina.Architecture.Tests;

public sealed class AuthenticationBypassRuleTests
{
    [Fact]
    public void NoProductionSourceReadsAnIdentityHandedToItByAnEdge()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            [.. AuthenticationBypasses.EdgeIdentityHeaders]));
    }

    [Fact]
    public void NoProductionSourceLetsAnEndpointExemptItselfFromTheDenial()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            [.. AuthenticationBypasses.AnonymityAttributes]));
    }

    [Fact]
    public void SigningOutNamesNoEndSessionEndpointBecauseItWouldSignTheOperatorOutOfEverythingElse()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            [.. AuthenticationBypasses.IdentityProviderSignOut]));
    }

    [Fact]
    public void NothingOnTheSignInAndSignOutPathCallsOutToAnyoneElse()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Api", "Controllers", "Auth"),
            [.. AuthenticationBypasses.OutboundCallers]));
    }

    [Fact]
    public void TheDriverAsksNobodyWhoTheyAreBecauseTheSocketPermissionsAreTheWholeGate()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Driver"),
            [.. AuthenticationBypasses.AskingWhoIsCalling]));
    }
}
