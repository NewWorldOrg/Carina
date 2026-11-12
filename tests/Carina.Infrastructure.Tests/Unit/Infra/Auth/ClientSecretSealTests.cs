using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class ClientSecretSealTests
{
    [Fact]
    public void ASealedSecretOpensWithTheKeysThatSealedIt()
    {
        ClientSecretSeal seal = SealingKeys.Seal();

        Assert.Equal(new ClientSecret("the-client-secret"), seal.Open(seal.Seal(new ClientSecret("the-client-secret"))));
    }

    [Fact]
    public void TheSealedFormDoesNotCarryTheSecret()
    {
        string held = SealingKeys.Seal().Seal(new ClientSecret("the-client-secret"));

        Assert.DoesNotContain("the-client-secret", held, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysKeptInTheirDirectoryOpenWhatWasSealedBeforeARestart()
    {
        DirectoryInfo keys = SealingKeys.Fresh();
        string held = SealingKeys.Seal(keys).Seal(new ClientSecret("the-client-secret"));

        Assert.Equal(new ClientSecret("the-client-secret"), SealingKeys.Seal(keys).Open(held));
    }

    [Fact]
    public void ASecretSealedWithKeysThatAreGoneDoesNotOpen()
    {
        string held = SealingKeys.Seal().Seal(new ClientSecret("the-client-secret"));

        Assert.Null(SealingKeys.Seal().Open(held));
    }

    [Fact]
    public void SomethingThatWasNeverSealedDoesNotOpen()
    {
        Assert.Null(SealingKeys.Seal().Open("the-client-secret"));
    }
}
