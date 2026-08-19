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
}
