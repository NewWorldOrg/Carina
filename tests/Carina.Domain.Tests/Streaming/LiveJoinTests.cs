using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveJoinTests
{
    [Fact]
    public void AViewerThatIsSeatedHasNoRefusal()
    {
        LiveJoin join = LiveJoin.Joined(new SeatedNowhere());

        Assert.True(join.Seated);
        Assert.NotNull(join.Viewing);
        Assert.Null(join.Refusal);
        Assert.Null(join.Ceiling);
        Assert.Empty(join.Note);
    }

    [Fact]
    public void AFullBudgetIsRefusedWithTheCeilingAndItsSentence()
    {
        TranscodeCeiling ceiling = new(4, 4);

        LiveJoin join = LiveJoin.Refused(ceiling);

        Assert.False(join.Seated);
        Assert.Equal(LiveRefusal.TooManyAlready, join.Refusal);
        Assert.Same(ceiling, join.Ceiling);
        Assert.Equal(ceiling.Said, join.Note);
    }

    [Fact]
    public void ARefusalWithoutASeatCarriesItsReasonAndNote()
    {
        LiveJoin join = LiveJoin.Refused(LiveRefusal.NoTunerFree, "every tuner is recording.");

        Assert.Equal(LiveRefusal.NoTunerFree, join.Refusal);
        Assert.Equal("every tuner is recording.", join.Note);
        Assert.Null(join.Ceiling);
    }

    [Fact]
    public void TooManyAlreadyCannotBeSaidWithoutTheCeiling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveJoin.Refused(LiveRefusal.TooManyAlready, "full"));
    }

    [Fact]
    public void AReasonOffTheListIsNotARefusal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveJoin.Refused((LiveRefusal)99, "made up"));
    }

    [Fact]
    public void ANoteNamingAPathOnThisMachineLosesThePath()
    {
        LiveJoin join = LiveJoin.Refused(LiveRefusal.TranscoderWouldNotStart, "could not open /usr/bin/ffmpeg here");

        Assert.DoesNotContain('/', join.Note);
    }

    private sealed class SeatedNowhere : ILiveViewing
    {
        public ChannelReader<LiveFrame> Frames { get; } = Channel.CreateUnbounded<LiveFrame>().Reader;

        public LiveBacklog Backlog => LiveBacklog.Empty;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
