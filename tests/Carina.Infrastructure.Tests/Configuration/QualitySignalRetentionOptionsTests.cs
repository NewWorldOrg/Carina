using Carina.Domain.Quality;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class QualitySignalRetentionOptionsTests
{
    [Fact(DisplayName = "BR-QD-006: raw samples are kept for a week unless the settings say otherwise")]
    public void RawSamplesAreKeptForAWeekUnlessTheSettingsSayOtherwise()
        => Assert.Equal(TimeSpan.FromDays(7), new QualitySignalOptions().Read().KeepSamplesFor);

    [Fact(DisplayName = "BR-QD-006: how long raw samples are kept is read as a duration")]
    public void HowLongRawSamplesAreKeptIsReadAsADuration()
        => Assert.Equal(
            TimeSpan.FromDays(3),
            new QualitySignalOptions { KeepSamplesFor = "3.00:00:00" }.Read().KeepSamplesFor);

    [Fact(DisplayName = "BR-QS-003: the minute windows are kept for ninety days and the hourly ones for as long as there is a system")]
    public void TheMinuteWindowsAreKeptForNinetyDaysAndTheHourlyOnesForAsLongAsThereIsASystem()
    {
        QualitySignalSettings read = new QualitySignalOptions().Read();

        Assert.Equal(TimeSpan.FromDays(90), read.KeptFor(QualityWindow.Minute));
        Assert.Null(read.KeptFor(QualityWindow.Hour));
    }

    [Fact(DisplayName = "BR-QS-003: how long each window layer is kept is read as a duration")]
    public void HowLongEachWindowLayerIsKeptIsReadAsADuration()
    {
        QualitySignalSettings read = new QualitySignalOptions
        {
            KeepMinuteWindowsFor = "30.00:00:00",
            KeepHourWindowsFor = "400.00:00:00",
        }.Read();

        Assert.Equal(TimeSpan.FromDays(30), read.KeptFor(QualityWindow.Minute));
        Assert.Equal(TimeSpan.FromDays(400), read.KeptFor(QualityWindow.Hour));
    }

    [Fact(DisplayName = "BR-QS-003: a window layer told to be kept forever is kept for as long as there is a system")]
    public void AWindowLayerToldToBeKeptForeverIsKeptForAsLongAsThereIsASystem()
    {
        QualitySignalSettings read = new QualitySignalOptions
        {
            KeepMinuteWindowsFor = "forever",
            KeepHourWindowsFor = "forever",
        }.Read();

        Assert.Null(read.KeptFor(QualityWindow.Minute));
        Assert.Null(read.KeptFor(QualityWindow.Hour));
    }

    [Fact(DisplayName = "BR-QS-003: minute windows kept for less time than the samples they hold are refused")]
    public void MinuteWindowsKeptForLessTimeThanTheSamplesTheyHoldAreRefused()
        => Assert.Throws<ArgumentException>(() => new QualitySignalOptions
        {
            KeepSamplesFor = "7.00:00:00",
            KeepMinuteWindowsFor = "1.00:00:00",
        }.Read());

    [Fact(DisplayName = "BR-QS-003: hour windows kept for less time than the minute ones are refused")]
    public void HourWindowsKeptForLessTimeThanTheMinuteOnesAreRefused()
        => Assert.Throws<ArgumentException>(() => new QualitySignalOptions
        {
            KeepMinuteWindowsFor = "90.00:00:00",
            KeepHourWindowsFor = "30.00:00:00",
        }.Read());

    [Fact(DisplayName = "BR-QD-006: samples kept for no time at all are refused")]
    public void SamplesKeptForNoTimeAtAllAreRefused()
        => Assert.Throws<ArgumentException>(() => new QualitySignalOptions { KeepSamplesFor = "00:00:00" }.Read());

    [Fact(DisplayName = "BR-QD-006: a retention that is not a duration is refused")]
    public void ARetentionThatIsNotADurationIsRefused()
        => Assert.Throws<ArgumentException>(() => new QualitySignalOptions { KeepSamplesFor = "a week" }.Read());

    [Fact(DisplayName = "BR-QD-006: a retention the settings could not hold is named at startup")]
    public void ARetentionTheSettingsCouldNotHoldIsNamedAtStartup()
    {
        ValidateOptionsResult refused = new QualitySignalValidation().Validate(
            null,
            new QualitySignalOptions { KeepMinuteWindowsFor = "1.00:00:00" });

        Assert.True(refused.Failed);
        Assert.Contains(nameof(QualitySignalOptions.KeepMinuteWindowsFor), refused.FailureMessage, StringComparison.Ordinal);
    }
}
