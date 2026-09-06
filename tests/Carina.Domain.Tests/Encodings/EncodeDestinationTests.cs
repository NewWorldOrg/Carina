using System.Reflection;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeDestinationTests
{
    private static readonly DateTime At = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoPartOfADestinationCanBeMovedFromOutside()
    {
        Assert.DoesNotContain(
            typeof(EncodeDestination).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.SetMethod is { IsPublic: true });
    }

    [Fact(DisplayName = "BR-EV-006: a change replaces every field the destination was made of")]
    public void AChangeReplacesEveryFieldTheDestinationWasMadeOf()
    {
        var another = new EncodeProfileId(Guid.NewGuid());
        EncodeDestination destination = Shelf();

        destination.Revise(new EncodeLabel("Another shelf"), new OutputRoot("elsewhere"), another);

        Assert.Equal("Another shelf", destination.Label.Value);
        Assert.Equal("elsewhere", destination.OutputRoot.Value);
        Assert.Equal(another, destination.DefaultProfileId);
        Assert.Equal(At, destination.DefinedAt);
    }

    [Fact(DisplayName = "BR-ED2-015: a destination that has not been retired says so, and says when once it has")]
    public void ADestinationSaysWhenItWasRetired()
    {
        EncodeDestination destination = Shelf();

        Assert.False(destination.IsRetired);
        Assert.Null(destination.RetiredAt);

        destination.Retire(At.AddDays(1));

        Assert.True(destination.IsRetired);
        Assert.Equal(At.AddDays(1), destination.RetiredAt);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired destination is neither changed nor retired a second time")]
    public void ARetiredDestinationIsNeitherChangedNorRetiredASecondTime()
    {
        EncodeDestination destination = Shelf();
        destination.Retire(At.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => destination.Revise(
            new EncodeLabel("Another shelf"),
            new OutputRoot("elsewhere"),
            new EncodeProfileId(Guid.NewGuid())));
        Assert.Throws<InvalidOperationException>(() => destination.Retire(At.AddDays(2)));
        Assert.Equal(At.AddDays(1), destination.RetiredAt);
    }

    [Fact]
    public void AnHourThatIsNotInUtcIsNotWhenADestinationWasRetired()
        => Assert.Throws<ArgumentException>(() => Shelf().Retire(new DateTime(2026, 9, 5, 3, 0, 0, DateTimeKind.Local)));

    private static EncodeDestination Shelf()
        => EncodeDestination.Define(
            new EncodeDestinationId(Guid.NewGuid()),
            new EncodeLabel("Shelf"),
            new OutputRoot("encodes"),
            new EncodeProfileId(Guid.NewGuid()),
            At);
}
