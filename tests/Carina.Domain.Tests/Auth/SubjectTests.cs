using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class SubjectTests
{
    [Fact]
    public void ASubjectKeepsExactlyWhatTheIdentityProviderCalledIt()
    {
        var subject = new Subject("d1f3c0a2-4b7e-4a1f-9a55-0f2b6c8e1234");

        Assert.Equal("d1f3c0a2-4b7e-4a1f-9a55-0f2b6c8e1234", subject.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySubjectIsNobody(string value)
    {
        Assert.Throws<ArgumentException>(() => new Subject(value));
    }

    [Fact]
    public void ASubjectIsNeverNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Subject(null!));
    }

    [Theory]
    [InlineData(" padded")]
    [InlineData("padded ")]
    public void SurroundingSpaceWouldMakeTwoRowsForOnePerson(string value)
    {
        Assert.Throws<ArgumentException>(() => new Subject(value));
    }

    [Fact]
    public void AControlCharacterWouldTravelIntoLogsAndScreens()
    {
        Assert.Throws<ArgumentException>(() => new Subject("sub\nject"));
    }

    [Fact]
    public void ASubjectLongerThanTheColumnIsRefusedBeforeTheDatabaseSeesIt()
    {
        string tooLong = new('s', Subject.LongestValue + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Subject(tooLong));
    }

    [Fact]
    public void ASubjectFillingTheColumnExactlyIsAccepted()
    {
        string longest = new('s', Subject.LongestValue);

        Assert.Equal(longest, new Subject(longest).Value);
    }

    [Fact]
    public void TwoSubjectsWithTheSameTextAreTheSamePerson()
    {
        Assert.Equal(new Subject("alice"), new Subject("alice"));
    }
}
