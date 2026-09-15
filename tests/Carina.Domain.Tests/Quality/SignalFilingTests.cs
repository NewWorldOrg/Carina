using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class SignalFilingTests
{
    private static readonly NetworkId Network = new(4);

    [Fact]
    public void ATuneIsFiledUnderTheStreamIntendedOnTheSameChannel()
    {
        IntendedStream on27 = Stream(27, 101);
        IntendedStream on30 = Stream(30, 102);

        Assert.Same(on27, SignalFiling.StreamFor([on30, on27], TuneParams.Terrestrial(27)));
    }

    [Fact]
    public void ATuneNoStreamIsIntendedOnIsFiledNowhere()
        => Assert.Null(SignalFiling.StreamFor([Stream(30, 102)], TuneParams.Terrestrial(27)));

    [Fact]
    public void AStreamWithNoServiceTakesNothing()
        => Assert.Null(SignalFiling.StreamFor([Stream(27)], TuneParams.Terrestrial(27)));

    [Fact]
    public void WhenTwoStreamsShareAChannelTheFirstInOrderTakesIt()
    {
        IntendedStream first = Stream(27, 101);
        IntendedStream second = Stream(27, 102);

        Assert.Same(first, SignalFiling.StreamFor([first, second], TuneParams.Terrestrial(27)));
    }

    [Fact]
    public void ACandidateIsFiledUnderTheSameStreamAsATuneOnItsChannel()
    {
        IReadOnlyList<IntendedStream> intended = [Stream(30, 102), Stream(27, 101)];

        Assert.Same(
            SignalFiling.StreamFor(intended, TuneParams.Terrestrial(27)),
            SignalFiling.StreamFor(intended, TuningParameters.Terrestrial(27)));
    }

    [Fact]
    public void TwoCandidatesOnOneChannelAreTheSameReception()
    {
        Assert.True(SignalFiling.Same(TuningParameters.Terrestrial(27), TuningParameters.Terrestrial(27)));
        Assert.False(SignalFiling.Same(TuningParameters.Terrestrial(27), TuningParameters.Terrestrial(30)));
    }

    private static IntendedStream Stream(int channel, params int[] services)
        => new(
            Network,
            null,
            TuningParameters.Terrestrial(channel),
            [.. services.Select(service => new ServiceId(service))],
            StreamReach.Reachable);
}
