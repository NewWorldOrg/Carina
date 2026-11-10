using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NotificationNameTests
{
    [Fact(DisplayName = "BR-NT-003: a name of 64 characters is kept and one of 65 is not")]
    public void ANameIsAtMostSixtyFourCharacters()
    {
        Assert.Equal(64, new NotificationName(new string('a', 64)).Value.Length);
        Assert.Throws<ArgumentException>(() => new NotificationName(new string('a', 65)));
    }

    [Theory(DisplayName = "BR-NT-003: a name is at least one character that can be seen")]
    [InlineData("")]
    [InlineData("   ")]
    public void ANameIsAtLeastOneCharacter(string name)
    {
        Assert.Throws<ArgumentException>(() => new NotificationName(name));
    }

    [Fact(DisplayName = "BR-NT-003: two destinations may carry the same name")]
    public void TwoNamesThatReadTheSameAreEqual()
    {
        Assert.Equal(new NotificationName("A channel"), new NotificationName(" A channel "));
    }

    [Fact]
    public void ANameHoldsNoControlCharacter()
    {
        Assert.Throws<ArgumentException>(() => new NotificationName("A\u0007channel"));
    }
}
