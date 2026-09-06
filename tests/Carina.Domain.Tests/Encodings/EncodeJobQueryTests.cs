using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeJobQueryTests
{
    [Fact]
    public void NothingAskedForIsTheFirstPageOfTheDefaultSizeOverEveryStanding()
    {
        EncodeJobQuery? query = EncodeJobQuery.For(null, null, null, null);

        Assert.NotNull(query);
        Assert.Empty(query.Statuses);
        Assert.Equal(1, query.Page);
        Assert.Equal(EncodeJobQuery.DefaultPerPage, query.PerPage);
    }

    [Fact]
    public void APageSizeOverTheCeilingIsCutDownToItAndOneBelowOneIsTheDefault()
    {
        Assert.Equal(EncodeJobQuery.MostPerPage, EncodeJobQuery.For(null, null, 1, EncodeJobQuery.MostPerPage + 1)!.PerPage);
        Assert.Equal(EncodeJobQuery.DefaultPerPage, EncodeJobQuery.For(null, null, 1, 0)!.PerPage);
        Assert.Equal(7, EncodeJobQuery.For(null, null, 3, 7)!.PerPage);
        Assert.Equal(3, EncodeJobQuery.For(null, null, 3, 7)!.Page);
    }

    [Fact]
    public void APageBelowTheFirstIsNoPageAtAll()
    {
        Assert.Null(EncodeJobQuery.For(null, null, 0, null));
        Assert.Null(EncodeJobQuery.For(null, null, -1, null));
    }

    [Fact(DisplayName = "BR-ES-002: the standings asked for are the ledger's own, once each, and nothing cast in from outside")]
    public void TheStandingsAskedForAreTheLedgersOwnOnceEach()
    {
        EncodeJobQuery? asked = EncodeJobQuery.For([EncodeJobStatus.Running, EncodeJobStatus.Queued, EncodeJobStatus.Running], null, null, null);

        Assert.NotNull(asked);
        Assert.Equal([EncodeJobStatus.Running, EncodeJobStatus.Queued], asked.Statuses);
        Assert.Null(EncodeJobQuery.For([(EncodeJobStatus)99], null, null, null));
    }

    [Fact(DisplayName = "BR-ES-002: a page of the ledger can be asked for one recording, and names none unless it is asked to")]
    public void APageOfTheLedgerCanBeAskedForOneRecording()
    {
        var recording = RecordingId.New();

        Assert.Null(EncodeJobQuery.For(null, null, null, null)!.Recording);
        Assert.Equal(recording, EncodeJobQuery.For(null, recording, null, null)!.Recording);
    }
}
