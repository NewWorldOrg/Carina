using System.Reflection;

namespace Carina.Contracts.Tests;

public sealed class SignalQualityMemberTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static SignalQualityDto Populated =>
        new()
        {
            Lock = SignalLock.Locked,
            CnrMilliDecibels = 21_500,
            PostViterbiBitErrors = [new LayerBitErrorCounts(0, 12, 1_000_000)],
            MeasuredAt = Moment,
            LockReadAt = Moment.AddMilliseconds(3),
            NotImplementedMetrics = [SignalQualityMetrics.PostViterbiBitError],
            MetricsOnAnotherScale = [SignalQualityMetrics.PostViterbiBitError],
        };

    private static IReadOnlyList<PropertyInfo> Settable =>
        [
            .. typeof(SignalQualityDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is not null)
                .OrderBy(property => property.Name, StringComparer.Ordinal),
        ];

    [Fact]
    public void EveryPropertyIsExercisedByTheFixtureThisSuiteCopiesFrom()
    {
        var empty = new SignalQualityDto();

        Assert.NotEmpty(Settable);
        Assert.All(
            Settable,
            property =>
                Assert.False(
                    Equals(property.GetValue(Populated), property.GetValue(empty)),
                    $"{property.Name} is not given a value here, so nothing in this suite would notice if it were dropped."
                )
        );
    }

    [Fact]
    public void EveryPropertySurvivesBeingCopied()
    {
        SignalQualityDto populated = Populated;
        SignalQualityDto copied = populated with { };

        Assert.All(
            Settable,
            property =>
                Assert.Equal(property.GetValue(populated), property.GetValue(copied))
        );
    }

    [Fact]
    public void EveryPropertyIsWeighedWhenTwoReadingsAreCompared()
    {
        SignalQualityDto populated = Populated;

        Assert.All(
            Settable,
            property =>
            {
                SignalQualityDto altered = populated with { };
                property.SetValue(altered, Different(property, property.GetValue(populated)));

                Assert.False(
                    populated.Equals(altered),
                    $"{property.Name} is not weighed by equality, so two readings that differ in it look like one."
                );
            }
        );
    }

    private static object? Different(PropertyInfo property, object? value) =>
        property.PropertyType switch
        {
            var type when type == typeof(SignalLock) => SignalLock.NotLocked,
            var type when type == typeof(int?) => (int?)((int?)value ?? 0) + 1,
            var type when type == typeof(DateTimeOffset?) =>
                ((DateTimeOffset?)value ?? Moment).AddMinutes(1),
            var type when type == typeof(IReadOnlyList<LayerBitErrorCounts>) =>
                (IReadOnlyList<LayerBitErrorCounts>)[new LayerBitErrorCounts(9, 9, 9)],
            var type when type == typeof(IReadOnlyList<string>) =>
                (IReadOnlyList<string>)[SignalQualityMetrics.Cnr],
            _ => throw new NotSupportedException(
                $"{property.Name} is a {property.PropertyType.Name}, which this suite does not know how to vary."
            ),
        };
}
