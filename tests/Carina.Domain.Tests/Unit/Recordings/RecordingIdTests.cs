using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingIdTests
{
    [Fact]
    public void ARecordingIdIsNotTheEmptyGuid()
        => Assert.Throws<ArgumentException>(() => new RecordingId(Guid.Empty));

    [Fact]
    public void ARecordingIdTravelsAsANameTheDriverWillAccept()
    {
        var id = new RecordingId(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));

        Assert.Equal("6f9619ff8b86d011b42d00c04fc964ff", id.Wire);
    }
}
