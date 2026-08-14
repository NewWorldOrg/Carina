namespace Carina.Contracts.Tests;

public sealed class DriverHelloTests
{
    private static DriverHello Hello(params string[] capabilities) =>
        new(DriverProtocol.Version, "b7f2c9", capabilities);

    [Fact]
    public void ReportedCapabilitiesAreSupported()
    {
        Assert.True(Hello(DriverCapabilities.Recording).Supports(DriverCapabilities.Recording));
    }

    [Fact]
    public void AbsentCapabilitiesAreNotSupported()
    {
        Assert.False(Hello(DriverCapabilities.Recording).Supports(DriverCapabilities.Live));
    }

    [Fact]
    public void UnknownCapabilityNamesAreCarriedWithoutComplaint()
    {
        var hello = Hello("somethingTheAppDoesNotKnow");

        Assert.False(hello.Supports(DriverCapabilities.Recording));
        Assert.Contains("somethingTheAppDoesNotKnow", hello.Capabilities);
    }

    [Fact]
    public void ADriverThatMeasuresOneMetricButNotAnotherSaysSo()
    {
        var hello = Hello(
            DriverCapabilities.SignalQuality,
            DriverCapabilities.SignalQualityMetric(SignalQualityMetrics.Cnr)
        );

        Assert.True(hello.Supports(DriverCapabilities.SignalQuality));
        Assert.True(hello.SupportsSignalQualityMetric(SignalQualityMetrics.Cnr));
        Assert.False(hello.SupportsSignalQualityMetric("signalStrength"));
        Assert.False(
            hello.SupportsSignalQualityMetric(SignalQualityMetrics.PostViterbiBitError)
        );
    }

    [Fact]
    public void ADriverThatMeasuresNothingLeavesEveryMetricUnsupported()
    {
        var hello = Hello(DriverCapabilities.Recording);

        Assert.False(hello.Supports(DriverCapabilities.SignalQuality));
        Assert.Empty(hello.DeclaredSignalQualityMetrics());
        Assert.All(
            SignalQualityMetrics.All,
            metric => Assert.False(hello.SupportsSignalQualityMetric(metric))
        );
    }

    [Fact]
    public void EveryMetricADriverDeclaresIsListedEvenTheOnesThisBuildDoesNotKnow()
    {
        var hello = Hello(
            DriverCapabilities.SignalQuality,
            DriverCapabilities.SignalQualityMetric(SignalQualityMetrics.Cnr),
            DriverCapabilities.SignalQualityMetric("somethingMeasuredLater")
        );

        Assert.Equal(
            new[] { SignalQualityMetrics.Cnr, "somethingMeasuredLater" },
            hello.DeclaredSignalQualityMetrics()
        );
        Assert.True(hello.SupportsSignalQualityMetric("somethingMeasuredLater"));
    }

    [Fact]
    public void TheCoarseCapabilityDoesNotStandInForAMetric()
    {
        var hello = Hello(DriverCapabilities.SignalQuality);

        Assert.True(hello.Supports(DriverCapabilities.SignalQuality));
        Assert.False(hello.SupportsSignalQualityMetric(SignalQualityMetrics.Cnr));
    }

    [Fact]
    public void ATunerMayBeToggledWhileTheDriverRunsOnlyWhenItSaysSo()
    {
        Assert.True(
            Hello(DriverCapabilities.LiveTunerToggle).Supports(DriverCapabilities.LiveTunerToggle)
        );
        Assert.False(Hello(DriverCapabilities.Live).Supports(DriverCapabilities.LiveTunerToggle));
    }

    [Fact]
    public void CapabilitiesAreNeverNull()
    {
        Assert.Empty(new DriverHello(DriverProtocol.Version, "b7f2c9", null!).Capabilities);
    }
}
