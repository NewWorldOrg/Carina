using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
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

    [Fact(DisplayName = "BR-NT-003: a name holds no control character")]
    public void ANameHoldsNoControlCharacter()
    {
        Assert.Throws<ArgumentException>(() => new NotificationName("A\u0007channel"));
    }

    [Theory(DisplayName = "BR-NT-003: a name written by a caller is read where it is one to sixty-four characters, and is nothing otherwise")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("A\u0007channel", null)]
    [InlineData(" A channel ", "A channel")]
    public void ANameWrittenByACallerIsReadOrNothing(string? written, string? read)
    {
        Assert.Equal(read, NotificationName.Read(written)?.Value);
        Assert.Equal(64, NotificationName.Read(new string('a', 64))?.Value.Length);
        Assert.Null(NotificationName.Read(new string('a', 65)));
    }
}
