using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingSessionsTests
{
    [Fact]
    public void ASessionCarriesTheNameOfTheRecordingItIsWriting()
    {
        var id = new RecordingId(new Guid("0192d4c1-6b7e-7f00-8000-000000000001"));

        Assert.Equal("rec-0192d4c16b7e7f008000000000000001", RecordingSessions.Named(id).Value);
    }

    [Fact]
    public void TheNameIsOneTheDriverWillTake()
    {
        SessionId named = RecordingSessions.Named(RecordingId.New());

        Assert.False(named.IsUnset);
        Assert.True(named.Value!.Length <= SessionId.MaxLength);
        Assert.Equal(named, SessionId.Parse(named.Value));
    }

    [Fact]
    public void TwoRecordingsNeverShareASession()
    {
        Assert.NotEqual(RecordingSessions.Named(RecordingId.New()), RecordingSessions.Named(RecordingId.New()));
    }

    [Fact]
    public void ARecordingIsNamedOrNothingIs()
    {
        Assert.Equal("id", Assert.Throws<ArgumentNullException>(() => RecordingSessions.Named(null!)).ParamName);
    }
}
