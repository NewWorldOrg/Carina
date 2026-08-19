namespace Carina.Architecture.Tests;

public sealed class ClientSecretRuleTests
{
    [Fact]
    public void TheClientSecretIsReadInTheClearOnlyWhereItIsStoredAndWhereItIsSpent()
    {
        Assert.Equal(
            [
                "Carina.Infrastructure/Auth/OidcGateway.cs",
                "Carina.Infrastructure/Persistence/Configurations/OidcSettingsConfiguration.cs",
            ],
            SourceScan.FilesMentioning(
                RepositoryLayout.SourceDirectory,
                [.. AuthenticationBypasses.ClientSecretInTheClear]));
    }

    [Fact]
    public void NothingThatReadsTheClientSecretInTheClearAlsoWritesToALog()
    {
        Assert.Empty(SourceScan.FilesMentioningBoth(
            RepositoryLayout.SourceDirectory,
            AuthenticationBypasses.ClientSecretInTheClear,
            AuthenticationBypasses.Logging));
    }

    [Fact]
    public void NothingThatAnswersACallerCanEvenNameTheClientSecret()
    {
        Assert.Empty(SourceScan.FilesMentioning(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Api", "Responder"),
            "ClientSecret"));
    }
}
