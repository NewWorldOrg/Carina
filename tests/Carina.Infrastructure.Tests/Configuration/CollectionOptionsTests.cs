using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class CollectionOptionsTests
{
    [Fact]
    public void NothingConfiguredLeavesEverySettingAtWhatItAlreadyWas()
    {
        CollectionSettings unset = new();
        CollectionSettings read = Read();

        Assert.Equal(unset, read);
    }

    [Fact]
    public void RidingAlongIsTurnedOffByTheSettingThatSaysSo()
        => Assert.False(Read(("RidesAlong", "false")).RidesAlong);

    [Fact]
    public void EverySweepSettingIsReadAsWritten()
    {
        CollectionSettings read = Read(
            ("BetweenSweeps", "00:20:00"),
            ("WantedCoverage", "7.00:00:00"),
            ("RevisitsBelow", "2.00:00:00"),
            ("BetweenVisits", "04:00:00"),
            ("BeforeRetrying", "01:30:00"),
            ("LongestVisit", "00:04:00"),
            ("KeepEndedProgrammes", "12:00:00"),
            ("ArchiveRetention", "90.00:00:00"),
            ("LongestBackOff", "18:00:00"),
            ("BetweenBoosts", "00:15:00"),
            ("LongestBoost", "00:45:00"),
            ("BetweenRideAlongSaves", "00:02:00"),
            ("BetweenSessionChecks", "00:00:20"));

        Assert.Equal(TimeSpan.FromMinutes(20), read.BetweenSweeps);
        Assert.Equal(TimeSpan.FromDays(7), read.WantedCoverage);
        Assert.Equal(TimeSpan.FromDays(2), read.RevisitsBelow);
        Assert.Equal(TimeSpan.FromHours(4), read.BetweenVisits);
        Assert.Equal(TimeSpan.FromMinutes(90), read.BeforeRetrying);
        Assert.Equal(TimeSpan.FromMinutes(4), read.LongestVisit);
        Assert.Equal(TimeSpan.FromHours(12), read.KeepEndedProgrammes);
        Assert.Equal(TimeSpan.FromDays(90), read.ArchiveRetention);
        Assert.Equal(TimeSpan.FromHours(18), read.LongestBackOff);
        Assert.Equal(TimeSpan.FromMinutes(15), read.BetweenBoosts);
        Assert.Equal(TimeSpan.FromMinutes(45), read.LongestBoost);
        Assert.Equal(TimeSpan.FromMinutes(2), read.BetweenRideAlongSaves);
        Assert.Equal(TimeSpan.FromSeconds(20), read.BetweenSessionChecks);
    }

    [Fact]
    public void AnArchiveNobodyBoundedIsKeptForever()
        => Assert.Null(Read().ArchiveRetention);

    [Fact]
    public void TheBackOffForAFullTunerIsReadAsWritten()
    {
        RotationBackoff read = Read(
            ("WhenTunersAreFull:FirstDelay", "00:00:45"),
            ("WhenTunersAreFull:Factor", "3"),
            ("WhenTunersAreFull:MaximumDelay", "00:10:00"),
            ("WhenTunersAreFull:FailureCeiling", "5")).WhenTunersAreFull;

        Assert.Equal(TimeSpan.FromSeconds(45), read.FirstDelay);
        Assert.Equal(3, read.Factor);
        Assert.Equal(TimeSpan.FromMinutes(10), read.MaximumDelay);
        Assert.Equal(5, read.FailureCeiling);
    }

    [Theory]
    [InlineData("BetweenSweeps", "half an hour")]
    [InlineData("BetweenSweeps", "00:00:00")]
    [InlineData("WantedCoverage", "-8.00:00:00")]
    [InlineData("LongestVisit", "00:00:00")]
    [InlineData("BeforeRetrying", "-01:00:00")]
    [InlineData("ArchiveRetention", "forever")]
    [InlineData("ArchiveRetention", "00:00:00")]
    [InlineData("RidesAlong", "sometimes")]
    [InlineData("BetweenSessionChecks", "20s")]
    public void ASettingThatCannotBeReadStopsTheProcessAndNamesItself(string name, string value)
    {
        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => Read((name, value)));

        Assert.Contains(
            $"{CollectionOptions.Section}:{name}",
            Assert.Single(refused.Failures),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Factor", "1")]
    [InlineData("FailureCeiling", "1")]
    [InlineData("MaximumDelay", "00:00:01")]
    [InlineData("Factor", "twice")]
    public void ABackOffThatCannotBackOffStopsTheProcessAndNamesItself(string name, string value)
    {
        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => Read(($"WhenTunersAreFull:{name}", value)));

        Assert.Contains(
            $"{CollectionOptions.Section}:WhenTunersAreFull",
            Assert.Single(refused.Failures),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RevisitsBelow", "9.00:00:00")]
    [InlineData("BeforeRetrying", "36:00:00")]
    public void ASettingThatContradictsAnotherStopsTheProcessAndNamesItself(string name, string value)
    {
        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => Read((name, value)));

        Assert.Contains(
            $"{CollectionOptions.Section}:{name}",
            Assert.Single(refused.Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASettingThatCannotBeReadStopsTheHostBeforeAnythingIsServed()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>(
                "ConnectionStrings:Carina", "Host=db;Database=carina;Username=carina;Password=placeholder"),
            new KeyValuePair<string, string?>(DriverOptions.SocketPathKey, "/run/carina/driver.sock"),
            new KeyValuePair<string, string?>($"{CollectionOptions.Section}:BetweenSweeps", "half an hour"),
        ]);
        builder.Services.AddCarinaInfrastructure(builder.Configuration);

        using IHost host = builder.Build();
        OptionsValidationException refusal = Assert.Throws<OptionsValidationException>(host.Start);

        Assert.Contains(
            $"{CollectionOptions.Section}:BetweenSweeps",
            refusal.Message,
            StringComparison.Ordinal);
    }

    private static CollectionSettings Read(params (string Name, string Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(
                    $"{CollectionOptions.Section}:{setting.Name}",
                    setting.Value)))
            .Build();

        ServiceProvider provider = new ServiceCollection()
            .AddCarinaInfrastructure(configuration)
            .BuildServiceProvider();

        using (provider)
        {
            return provider.GetRequiredService<CollectionSettings>();
        }
    }
}
