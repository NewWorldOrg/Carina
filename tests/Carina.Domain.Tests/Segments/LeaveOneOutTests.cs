using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LeaveOneOutTests
{
    [Fact(DisplayName = "BR-CD-008: a fold masks the held-out recording's samples and hands every other recording's over as they are, in order, those labelled again without the entries set aside for it in their place")]
    public void AFoldMasksTheHeldOutSamples()
    {
        SegmentSample[] first = [Sample(1), Sample(2)];
        SegmentSample[] heldOut = [Sample(3)];
        SegmentSample[] standingOnIt = [Sample(4), Sample(5)];
        SegmentSample[] readAgain = [Sample(6)];
        SegmentSample[] last = [Sample(7)];

        IReadOnlyList<SegmentSample> fold = LeaveOneOut.Samples([first, heldOut, standingOnIt, last], 1, new Dictionary<int, IReadOnlyList<SegmentSample>> { [2] = readAgain });

        Assert.Equal([first[0], first[1], readAgain[0], last[0]], fold);
        Assert.Same(first[0], fold[0]);
        Assert.Same(last[0], fold[3]);
        Assert.DoesNotContain(heldOut[0], fold);
    }

    [Fact(DisplayName = "the held-out recording is never read again in its own fold, and only recordings of the round are")]
    public void OnlyAnotherRecordingIsReadAgain()
    {
        SegmentSample[][] each = [[Sample(1)], [Sample(2)]];

        Assert.Throws<ArgumentException>(() => LeaveOneOut.Samples(each, 0, new Dictionary<int, IReadOnlyList<SegmentSample>> { [0] = [Sample(3)] }));
        Assert.Throws<ArgumentException>(() => LeaveOneOut.Samples(each, 0, new Dictionary<int, IReadOnlyList<SegmentSample>> { [2] = [Sample(3)] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => LeaveOneOut.Samples(each, 2, new Dictionary<int, IReadOnlyList<SegmentSample>>()));
    }

    private static SegmentSample Sample(double length)
        => new(SegmentFeatureValues.Of(new Dictionary<string, double?> { [SegmentFeature.Length] = length }), "1:1", null, SegmentKind.Programme, 1.0);
}
