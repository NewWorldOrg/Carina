using Carina.Domain.Streaming;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class TranscodingOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansTheDefaultCeiling()
    {
        TranscodeBudgetSettings read = Read();

        Assert.Equal(new TranscodeBudgetSettings().AtOnce, read.AtOnce);
    }

    [Fact]
    public void TheCeilingReachesTheBudgetThatUsesIt()
    {
        Assert.Equal(6, Read(("Transcoding:AtOnce", "6")).AtOnce);
        Assert.Equal(1, Read(("Transcoding:AtOnce", "1")).AtOnce);
    }

    [Theory]
    [InlineData("Transcoding:AtOnce", "0")]
    [InlineData("Transcoding:AtOnce", "-1")]
    [InlineData("Transcoding:AtOnce", "four")]
    [InlineData("Transcoding:AtOnce", "2.5")]
    public void ACeilingNothingCouldRunUnderIsRefusedByName(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Equal("AtOnce", refusal.ParamName);
        Assert.Contains("Transcoding:AtOnce", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingConfiguredMeansTheSoftwareEncoder()
    {
        Assert.Equal(LiveEncoder.Software, ReadLive().Prefer);
        Assert.Equal(new LiveTranscodeSettings().Prefer, ReadLive().Prefer);
    }

    [Theory]
    [InlineData("Software", LiveEncoder.Software)]
    [InlineData("Vaapi", LiveEncoder.Vaapi)]
    public void TheEncoderAskedForReachesTheSettingsTheTranscoderReads(string named, LiveEncoder read)
    {
        Assert.Equal(read, ReadLive(("Transcoding:Prefer", named)).Prefer);
    }

    [Theory]
    [InlineData("vaapi")]
    [InlineData("VAAPI")]
    [InlineData("2")]
    [InlineData("hardware")]
    [InlineData("Software ")]
    public void AnEncoderOffTheListIsRefusedByNameAndNamesTheOnesThereAre(string named)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => ReadLive(("Transcoding:Prefer", named)));

        Assert.Equal("Prefer", refusal.ParamName);
        Assert.Contains("Transcoding:Prefer", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Software, Vaapi", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingConfiguredMeansCaptionsAreNotMovedAgainstThePicture()
    {
        Assert.Equal(TimeSpan.Zero, ReadCaptions().EncoderDelay);
    }

    [Theory]
    [InlineData("00:00:00.300", 300)]
    [InlineData("-00:00:00.300", -300)]
    [InlineData("00:00:02", 2_000)]
    public void TheCaptionDelayAskedForReachesTheSettingsTheCaptionerReads(string named, int milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), ReadCaptions(("Transcoding:CaptionDelay", named)).EncoderDelay);
    }

    [Theory]
    [InlineData("300")]
    [InlineData("300ms")]
    [InlineData("soon")]
    public void ACaptionDelayThatIsNotASpanOfTimeIsRefusedByName(string named)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => ReadCaptions(("Transcoding:CaptionDelay", named)));

        Assert.Equal("CaptionDelay", refusal.ParamName);
        Assert.Contains("Transcoding:CaptionDelay", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:11")]
    [InlineData("-00:00:11")]
    public void ACaptionDelayFurtherThanTenSecondsIsRefusedByName(string named)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => ReadCaptions(("Transcoding:CaptionDelay", named)));

        Assert.Equal("CaptionDelay", refusal.ParamName);
        Assert.Contains("Transcoding:CaptionDelay", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaptionDelayOffTheClockIsWhatStopsTheProcessStartingToo()
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(("Transcoding:CaptionDelay", "soon")));

        ValidateOptionsResult validated = new TranscodingValidation().Validate(null, options);

        Assert.True(validated.Failed);
        Assert.Contains("Transcoding:CaptionDelay", validated.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEncoderOffTheListIsWhatStopsTheProcessStartingToo()
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(("Transcoding:Prefer", "hardware")));

        ValidateOptionsResult validated = new TranscodingValidation().Validate(null, options);

        Assert.True(validated.Failed);
        Assert.Contains("Transcoding:Prefer", validated.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatTheEncoderSettingLeavesAloneStaysAsItWas()
    {
        LiveTranscodeSettings read = ReadLive(("Transcoding:Prefer", "Vaapi"));
        LiveTranscodeSettings unset = new();

        Assert.Equal(unset.Programme, read.Programme);
        Assert.Equal(unset.LongestProbe, read.LongestProbe);
        Assert.Equal(unset.StopGrace, read.StopGrace);
    }

    [Fact]
    public void TheSameRefusalIsWhatStopsTheProcessStarting()
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(("Transcoding:AtOnce", "0")));

        Assert.True(new TranscodingValidation().Validate(null, options).Failed);
        Assert.True(new TranscodingValidation().Validate(null, new TranscodingOptions()).Succeeded);
    }

    [Fact]
    public void ValidatingNothingIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new TranscodingValidation().Validate(null, null!));
        Assert.Throws<ArgumentNullException>(() => new TranscodingOptions().ReadFrom(null!));
    }

    private static TranscodeBudgetSettings Read(params (string Key, string Value)[] settings)
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static LiveTranscodeSettings ReadLive(params (string Key, string Value)[] settings)
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(settings));

        return options.ReadLive();
    }

    private static LiveCaptionSettings ReadCaptions(params (string Key, string Value)[] settings)
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(settings));

        return options.ReadCaptions();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
