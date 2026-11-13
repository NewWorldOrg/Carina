using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Segments;

[Trait("Category", "Unit")]
public sealed class ViewerActionIntakeDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private readonly HeldSegmentModelVersions versions = new();

    private readonly HeldSkipEvents skips = new();

    private readonly HeldSeekEvents seeks = new();

    private ViewerActionIntakeDomainService Service => new(versions, skips, seeks);

    [Fact(DisplayName = "BR-CV-003: an event counts towards the hour of another only when it happened later than an hour before it")]
    public void AnEventCountsOnlyWhenLaterThanAnHourBefore()
    {
        SkipEvent skip = Skip(RecordingId.New(), Noon);

        Assert.False(skip.CountsWith(Noon - SkipEvent.Within));
        Assert.True(skip.CountsWith(Noon - SkipEvent.Within + TimeSpan.FromTicks(10)));
        Assert.True(skip.CountsWith(Noon));
        Assert.True(skip.CountsWith(Noon.AddMinutes(5)));
    }

    [Fact(DisplayName = "BR-CV-003: an event is taken beside fewer than the most an hour that count with it, and refused beside as many")]
    public void AnEventIsTakenBesideFewerThanTheMost()
    {
        SkipEvent skip = Skip(RecordingId.New(), Noon);
        DateTime[] counted = [.. Enumerable.Repeat(Noon.AddMinutes(-30), SkipEvent.MostAnHour - 1)];

        Assert.True(skip.IsTakenBeside(counted));
        Assert.False(skip.IsTakenBeside([.. counted, Noon.AddMinutes(-1)]));
        Assert.True(skip.IsTakenBeside([.. counted, Noon - SkipEvent.Within]));
    }

    [Fact(DisplayName = "BR-CV-004: a seek counts towards the hour of another as a skip event does")]
    public void ASeekCountsAsASkipDoes()
    {
        SeekEvent seek = Seek(RecordingId.New(), Noon);
        DateTime[] counted = [.. Enumerable.Repeat(Noon.AddMinutes(-30), SeekEvent.MostAnHour - 1)];

        Assert.False(seek.CountsWith(Noon - SeekEvent.Within));
        Assert.True(seek.CountsWith(Noon - SeekEvent.Within + TimeSpan.FromTicks(10)));
        Assert.True(seek.IsTakenBeside(counted));
        Assert.False(seek.IsTakenBeside([.. counted, Noon]));
    }

    [Theory(DisplayName = "BR-CV-004: a seek goes its way when it lands later going forward and earlier coming back")]
    [InlineData(SeekAction.Forward, 600, 690, true)]
    [InlineData(SeekAction.Forward, 600, 600, false)]
    [InlineData(SeekAction.Forward, 600, 590, false)]
    [InlineData(SeekAction.Returned, 690, 600, true)]
    [InlineData(SeekAction.Returned, 600, 600, false)]
    [InlineData(SeekAction.Returned, 600, 610, false)]
    public void ASeekGoesItsWay(SeekAction action, double from, double to, bool goes)
        => Assert.Equal(goes, ViewerSeek.GoesItsWay(action, TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to)));

    [Fact(DisplayName = "BR-CV-003: a skip button naming a version kept, or none for a chapter an encode marked, could have been offered, and one naming a version no longer kept could not")]
    public async Task OnlyAVersionKeptOffersAButton()
    {
        Assert.True(await Service.IsOfferedByAVersionKeptAsync(versions.Active.Number, Cancel));
        Assert.True(await Service.IsOfferedByAVersionKeptAsync(null, Cancel));
        Assert.False(await Service.IsOfferedByAVersionKeptAsync(99, Cancel));
        Assert.False(await Service.IsOfferedByAVersionKeptAsync(0, Cancel));
    }

    [Fact(DisplayName = "BR-CV-003: a skip event past the most an hour of its recording is refused, one an hour after the first is taken, and another recording's is taken")]
    public async Task ASkipPastTheMostAnHourIsRefused()
    {
        RecordingId recording = RecordingId.New();

        for (int taken = 0; taken < SkipEvent.MostAnHour; taken++)
        {
            Assert.True(await Service.TakeAsync(Skip(recording, Noon.AddSeconds(taken)), Cancel));
        }

        Assert.False(await Service.TakeAsync(Skip(recording, Noon.AddMinutes(30)), Cancel));
        Assert.True(await Service.TakeAsync(Skip(recording, Noon + SkipEvent.Within), Cancel));
        Assert.True(await Service.TakeAsync(Skip(RecordingId.New(), Noon.AddMinutes(30)), Cancel));
        Assert.Equal(SkipEvent.MostAnHour + 2, skips.Taken.Count);
    }

    [Fact(DisplayName = "BR-CV-004: a seek past the most an hour of its recording is refused")]
    public async Task ASeekPastTheMostAnHourIsRefused()
    {
        RecordingId recording = RecordingId.New();

        for (int taken = 0; taken < SeekEvent.MostAnHour; taken++)
        {
            Assert.True(await Service.TakeAsync(Seek(recording, Noon.AddSeconds(taken)), Cancel));
        }

        Assert.False(await Service.TakeAsync(Seek(recording, Noon.AddMinutes(30)), Cancel));
        Assert.Equal(SeekEvent.MostAnHour, seeks.Taken.Count);
    }

    private static SkipEvent Skip(RecordingId recording, DateTime at)
        => SkipEvent.Of(recording, new ViewerAction(at, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), null));

    private static SeekEvent Seek(RecordingId recording, DateTime at)
        => SeekEvent.Of(recording, new ViewerSeek(at, SeekAction.Forward, SeekMeans.Bar, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690)));
}
