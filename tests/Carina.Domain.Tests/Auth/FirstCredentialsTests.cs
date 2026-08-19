using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class FirstCredentialsTests
{
    [Fact]
    public void TheFirstUsernameIsOneTheAccountAccepts()
    {
        LocalAccount account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            PasswordHash.Encode(
                PasswordHashPolicy.Default,
                new byte[PasswordHashPolicy.Default.SaltLength],
                new byte[PasswordHashPolicy.Default.DigestLength]),
            DateTime.UtcNow);

        Assert.Equal(FirstCredentials.Username, account.Username);
    }

    [Fact]
    public void EveryMadePasswordIsDifferentFromTheLast()
    {
        string[] made = [.. Enumerable.Range(0, 32).Select(_ => FirstCredentials.MakePassword())];

        Assert.Equal(made.Length, made.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AMadePasswordCarriesTheWholeOfItsRandomnessInPlainLetters()
    {
        string made = FirstCredentials.MakePassword();

        Assert.Equal(32, made.Length);
        Assert.All(made, letter => Assert.True(
            char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_',
            $"{letter} needs escaping when it is read out of a log."));
    }
}
