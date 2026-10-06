using System.Buffers.Text;
using System.Text;

using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingCursorTests
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RecordingId Last = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    [Theory(DisplayName = "D-11: a position read back from what it was written as is the same position")]
    [InlineData(RecordingSort.StartedAt, true)]
    [InlineData(RecordingSort.StartedAt, false)]
    [InlineData(RecordingSort.ProgrammeStartsAt, true)]
    [InlineData(RecordingSort.ProgrammeStartsAt, false)]
    public void APositionReadBackFromWhatItWasWrittenAsIsTheSamePosition(RecordingSort sort, bool descending)
    {
        RecordingCursor written = RecordingCursor.After(sort, descending, Noon.AddTicks(10), Last);

        RecordingCursor read = Assert.IsType<RecordingCursor>(RecordingCursor.Read(written.Wire));

        Assert.Equal(sort, read.Sort);
        Assert.Equal(descending, read.Descending);
        Assert.Equal(Noon.AddTicks(10), read.Key);
        Assert.Equal(DateTimeKind.Utc, read.Key.Kind);
        Assert.Equal(Last, read.Id);
        Assert.Equal(written.Wire, read.Wire);
    }

    [Fact(DisplayName = "D-11: a position is written in letters a query string carries as they are")]
    public void APositionIsWrittenInLettersAQueryStringCarriesAsTheyAre()
    {
        string wire = RecordingCursor.After(RecordingSort.StartedAt, true, Noon, Last).Wire;

        Assert.Matches("^[A-Za-z0-9_-]+$", wire);
    }

    [Theory(DisplayName = "D-11: something that is not a position written here is not read as one")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not a position")]
    [InlineData("%%%")]
    public void SomethingThatIsNotAPositionIsNotReadAsOne(string wire)
        => Assert.Null(RecordingCursor.Read(wire));

    [Theory(DisplayName = "D-11: a position whose parts are not the ones written here is not read as one")]
    [InlineData("2:0:d:638896896000000000:0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("1:9:d:638896896000000000:0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("1:0:x:638896896000000000:0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("1:0:d:-1:0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("1:0:d:noon:0f8fad5bd9cb469fa16570867728950e")]
    [InlineData("1:0:d:638896896000000000:not-an-id")]
    [InlineData("1:0:d:638896896000000000:00000000000000000000000000000000")]
    [InlineData("1:0:d:638896896000000000")]
    [InlineData("1:0:d:638896896000000000:0f8fad5bd9cb469fa16570867728950e:more")]
    public void APositionWhosePartsAreNotTheOnesWrittenHereIsNotReadAsOne(string inside)
        => Assert.Null(RecordingCursor.Read(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(inside))));

    [Fact(DisplayName = "D-11: a query that carries on after a position takes it, and asks for no page number")]
    public void AQueryThatCarriesOnAfterAPositionTakesIt()
    {
        RecordingCursor after = RecordingCursor.After(RecordingSort.StartedAt, true, Noon, Last);

        RecordingQuery query = Assert.IsType<RecordingQuery>(
            RecordingQuery.For(null, null, RecordingSort.StartedAt, descending: true, after: after));

        Assert.Equal(after, query.After);
    }

    [Fact(DisplayName = "D-11: a query that names a page number and a position at once asks for two places and is refused")]
    public void AQueryThatNamesAPageNumberAndAPositionAtOnceIsRefused()
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            RecordingSort.StartedAt,
            descending: true,
            page: 2,
            after: RecordingCursor.After(RecordingSort.StartedAt, true, Noon, Last)));

    [Theory(DisplayName = "D-11: a position written for another order is refused rather than read against this one")]
    [InlineData(RecordingSort.StartedAt, false)]
    [InlineData(RecordingSort.ProgrammeStartsAt, true)]
    public void APositionWrittenForAnotherOrderIsRefused(RecordingSort sort, bool descending)
        => Assert.Null(RecordingQuery.For(
            null,
            null,
            sort,
            descending,
            after: RecordingCursor.After(RecordingSort.StartedAt, true, Noon, Last)));
}
