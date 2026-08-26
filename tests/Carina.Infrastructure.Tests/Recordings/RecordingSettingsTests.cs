using Carina.Domain.Recordings;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingSettingsTests
{
    [Fact]
    public void TheRecorderComesWithATickItCanActuallyMeet()
    {
        RecordingSettings unset = RecordingSettings.Default;

        Assert.Equal(TimeSpan.FromSeconds(10), unset.BeforeFirstTick);
        Assert.Equal(TimeSpan.FromSeconds(5), unset.BetweenTicks);
        Assert.Equal(TimeSpan.FromSeconds(25), unset.TuningLead);
        Assert.Equal("primary", unset.OutputRoot.Value);
        Assert.Equal(".ts", RecordingSettings.FileExtension);
    }

    [Fact]
    public void TheHeadIsEveryWaitBetweenBecomingDueAndTheFirstByteLanding()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), RecordingSettings.NoticingItIsDue);
        Assert.Equal(TimeSpan.FromSeconds(10), RecordingSettings.WaitingForASeat);
        Assert.Equal(TimeSpan.FromSeconds(5), RecordingSettings.WaitingForALock);
        Assert.Equal(TimeSpan.FromSeconds(5), RecordingSettings.WaitingForTheFirstByte);

        Assert.Equal(
            RecordingSettings.NoticingItIsDue
                + RecordingSettings.WaitingForASeat
                + RecordingSettings.WaitingForALock
                + RecordingSettings.WaitingForTheFirstByte,
            RecordingSettings.LongestWayToTheFirstByte);
        Assert.Equal(RecordingSettings.LongestWayToTheFirstByte, RecordingSettings.Default.TuningLead);
        Assert.Equal(RecordingSettings.NoticingItIsDue, RecordingSettings.Default.BetweenTicks);
    }

    [Fact]
    public void AHeadNoLongerThanTheGapBetweenTicksIsRefused()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => Built(lead: TimeSpan.FromSeconds(5)));

        Assert.Equal("tuningLead", refused.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Built(lead: TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1)));
        Assert.Equal(
            TimeSpan.FromSeconds(5) + TimeSpan.FromTicks(1),
            Built(lead: TimeSpan.FromSeconds(5) + TimeSpan.FromTicks(1)).TuningLead);
    }

    [Fact]
    public void ATickWithNoGapBetweenItsTurnsIsRefused()
    {
        Assert.Equal(
            "betweenTicks",
            Assert.Throws<ArgumentOutOfRangeException>(() => Built(between: TimeSpan.Zero)).ParamName);
        Assert.Equal(
            "beforeFirstTick",
            Assert.Throws<ArgumentOutOfRangeException>(() => Built(before: TimeSpan.Zero)).ParamName);
        Assert.Equal(
            "outputRoot",
            Assert.Throws<ArgumentNullException>(
                () => new RecordingSettings(
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    null!)).ParamName);
    }

    [Fact]
    public void ConfigurationThatSaysNothingLeavesTheRecorderAsItComes()
    {
        RecordingSettings read = Read(new Dictionary<string, string?>());

        Assert.Equal(RecordingSettings.Default.BetweenTicks, read.BetweenTicks);
        Assert.Equal(RecordingSettings.Default.TuningLead, read.TuningLead);
        Assert.Equal(RecordingSettings.Default.OutputRoot, read.OutputRoot);
    }

    [Fact]
    public void EverySettingTheSectionCarriesIsRead()
    {
        RecordingSettings read = Read(new Dictionary<string, string?>
        {
            ["Recording:BeforeFirstTick"] = "00:00:20",
            ["Recording:BetweenTicks"] = "00:00:02",
            ["Recording:TuningLead"] = "00:00:30",
            ["Recording:OutputRoot"] = "archive",
        });

        Assert.Equal(TimeSpan.FromSeconds(20), read.BeforeFirstTick);
        Assert.Equal(TimeSpan.FromSeconds(2), read.BetweenTicks);
        Assert.Equal(TimeSpan.FromSeconds(30), read.TuningLead);
        Assert.Equal("archive", read.OutputRoot.Value);
    }

    [Fact]
    public void ASettingTheRecorderCouldNotRunOnIsNamedAtStartup()
    {
        Assert.Equal(
            "tuningLead",
            Assert.Throws<ArgumentException>(
                () => Read(new Dictionary<string, string?> { ["Recording:TuningLead"] = "00:00:01" })).ParamName);

        Assert.Equal(
            "BetweenTicks",
            Assert.Throws<ArgumentException>(
                () => Read(new Dictionary<string, string?> { ["Recording:BetweenTicks"] = "every minute" })).ParamName);

        Assert.Equal(
            "OutputRoot",
            Assert.Throws<ArgumentException>(
                () => Read(new Dictionary<string, string?> { ["Recording:OutputRoot"] = "/srv/recordings" })).ParamName);
    }

    [Fact]
    public void TheValidationSaysWhichSettingItRefused()
    {
        var options = new RecordingOptions();
        options.ReadFrom(Configuration(new Dictionary<string, string?> { ["Recording:OutputRoot"] = "/srv" }));

        Assert.False(new RecordingValidation().Validate(null, options).Succeeded);
        Assert.True(new RecordingValidation().Validate(null, new RecordingOptions()).Succeeded);
    }

    private static RecordingSettings Built(
        TimeSpan? before = null,
        TimeSpan? between = null,
        TimeSpan? lead = null)
        => new(
            before ?? TimeSpan.FromSeconds(10),
            between ?? TimeSpan.FromSeconds(5),
            lead ?? TimeSpan.FromSeconds(15),
            new OutputRoot("primary"));

    private static RecordingSettings Read(IDictionary<string, string?> settings)
    {
        var options = new RecordingOptions();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static IConfiguration Configuration(IDictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
