using Carina.Api.Responder.Segments;
using Carina.Domain.Segments;

namespace Carina.Api.Tests.Unit.App.Segments;

[Trait("Category", "Unit")]
public sealed class SegmentMetricsResponderTests
{
    [Fact(DisplayName = "BR-CD-008: the figures of the version in use answer the seconds of breaks, openings and endings found, each kind in a row of its own")]
    public void TheStatusAnswersEveryKindMeasured()
    {
        SegmentMetrics metrics = new(
            0.9,
            0.8,
            [new KindMetrics(SegmentKind.Break, 0.95), new KindMetrics(SegmentKind.Opening, 0.5), new KindMetrics(SegmentKind.Ending, null)],
            null,
            0);

        Assert.Equal(
            [
                new SegmentKindMetricsResponder(SegmentKind.Break, 0.95),
                new SegmentKindMetricsResponder(SegmentKind.Opening, 0.5),
                new SegmentKindMetricsResponder(SegmentKind.Ending, null),
            ],
            SegmentMetricsResponder.Of(metrics).Kinds);
    }

    [Fact(DisplayName = "BR-CD-008: a version evaluated before openings and endings were measured answers the row for breaks alone")]
    public void AVersionEvaluatedBeforeOpeningsAndEndingsWereMeasuredAnswersBreaksAlone()
    {
        SegmentMetrics metrics = new(0.9, 0.8, [new KindMetrics(SegmentKind.Break, 0.95)], null, 0);

        Assert.Equal([new SegmentKindMetricsResponder(SegmentKind.Break, 0.95)], SegmentMetricsResponder.Of(metrics).Kinds);
    }
}
