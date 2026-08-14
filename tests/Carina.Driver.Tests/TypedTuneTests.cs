using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class TypedTuneTests
{
    private const int UnassignedTerrestrialChannel = 53;

    private const int SyntheticStream = 50_001;

    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    [Fact]
    public void ATerrestrialTuneIsBuiltFromTheTypedChannelNumber()
    {
        var tune = TuneParams.Terrestrial(UnassignedTerrestrialChannel);

        var channel = Assert.IsType<TerrestrialChannel>(
            DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );

        Assert.Equal(UnassignedTerrestrialChannel, channel.PhysicalChannel);
    }

    [Fact]
    public void ABroadcastSatelliteTuneIsBuiltFromTheTypedSlotAndTheStreamItNames()
    {
        var tune = TuneParams.Bs(15, SyntheticStream);

        var channel = Assert.IsType<BroadcastSatelliteChannel>(
            DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );

        Assert.Equal(15, channel.Slot);
        Assert.Equal(SyntheticStream, channel.TransportStreamId);
    }

    [Fact]
    public void ACommunicationSatelliteTuneIsBuiltFromTheTypedSlotAndNamesNoStream()
    {
        var tune = TuneParams.Cs110(24);

        var channel = Assert.IsType<CommunicationSatelliteChannel>(
            DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );

        Assert.Equal(24, channel.Slot);
    }

    [Fact]
    public void TheTypedParametersTuneTheFrontendEvenWhenTheOlderFieldNamesAnotherChannel()
    {
        var channel = Assert.IsType<TerrestrialChannel>(
            DvbTuneRequest.Resolve(
                TuneParams.Terrestrial(UnassignedTerrestrialChannel),
                new TuningRequest(TunerKind.Terrestrial, 27)
            )
        );

        Assert.Equal(UnassignedTerrestrialChannel, channel.PhysicalChannel);
    }

    [Fact]
    public void ATuneOnASystemThisDriverDoesNotKnowIsRefusedRatherThanGuessedAt()
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () =>
                DvbTuneRequest.Resolve(
                    new TuneParams(),
                    new TuningRequest(TunerKind.Terrestrial, 27)
                )
        );

        Assert.Contains("system", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TuneSystem.IsdbT)]
    [InlineData(TuneSystem.IsdbSBs)]
    [InlineData(TuneSystem.IsdbSCs110)]
    public void ATunedSystemWithoutItsOwnParametersIsRefusedRatherThanTunedAtChannelZero(
        TuneSystem system
    )
    {
        var refusal = Assert.Throws<DvbDeviceException>(
            () =>
                DvbTuneRequest.Resolve(
                    new TuneParams { System = system },
                    new TuningRequest(TunerKind.Terrestrial, 27)
                )
        );

        Assert.Contains(
            TuneSystemConverter.WireName(system),
            refusal.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(12)]
    [InlineData(63)]
    [InlineData(0)]
    public void ATerrestrialChannelOutsideThePlanIsRefusedHereEvenThoughTheContractChecksItFirst(
        int physicalChannel
    )
    {
        var tune = new TuneParams
        {
            System = TuneSystem.IsdbT,
            IsdbT = new IsdbTParams(physicalChannel),
        };

        Assert.NotEmpty(tune.Validate());
        Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );
    }

    [Theory]
    [InlineData(7)]
    [InlineData(17)]
    [InlineData(2)]
    [InlineData(25)]
    public void ABroadcastSatelliteSlotOutsideThePlanIsRefusedHereTooSoNoCallerCanSkipTheCheck(
        int bsChannel
    )
    {
        var tune = new TuneParams
        {
            System = TuneSystem.IsdbSBs,
            IsdbSBs = new IsdbSBsParams(bsChannel, SyntheticStream),
        };

        Assert.NotEmpty(tune.Validate());
        Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(26)]
    public void ACommunicationSatelliteSlotOutsideThePlanIsRefusedHereToo(int csChannel)
    {
        var tune = new TuneParams
        {
            System = TuneSystem.IsdbSCs110,
            IsdbSCs110 = new IsdbSCs110Params(csChannel),
        };

        Assert.NotEmpty(tune.Validate());
        Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );
    }

    [Fact]
    public void ATransportStreamIdWiderThanSixteenBitsIsRefusedHereToo()
    {
        var tune = new TuneParams
        {
            System = TuneSystem.IsdbSBs,
            IsdbSBs = new IsdbSBsParams(15, 70_000),
        };

        Assert.NotEmpty(tune.Validate());
        Assert.Throws<DvbDeviceException>(
            () => DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest())
        );
    }

    [Fact]
    public void TheDriverAcceptsTheWholeRangeTheBroadcastStandardsAccept()
    {
        for (var number = -1; number <= 300; number++)
        {
            Assert.Equal(
                BroadcastStandards.IsTerrestrialChannel(number),
                Accepts(TuneParams.Terrestrial(number))
            );
            Assert.Equal(
                BroadcastStandards.IsBsChannel(number),
                Accepts(TuneParams.Bs(number, SyntheticStream))
            );
            Assert.Equal(
                BroadcastStandards.IsCs110Channel(number),
                Accepts(TuneParams.Cs110(number))
            );
        }
    }

    [Fact]
    public void ATypedTerrestrialTuneReachesTheFrontendAsTheTerrestrialPropertyList()
    {
        var (calls, clock) = Ready();
        var tune = TuneParams.Terrestrial(UnassignedTerrestrialChannel);

        using var device = Factory(calls, clock)
            .Create(Terrestrial(), tune.ToLegacyRequest(), tune);

        var properties = Assert.Single(calls.PropertiesSet);

        Assert.Equal(
            (uint)DeliverySystem.IsdbTerrestrial.Code,
            ValueOf(properties, DvbProperty.DeliverySystem)
        );
        Assert.Equal(
            (uint)BroadcastStandards.TerrestrialCentreHz(UnassignedTerrestrialChannel),
            ValueOf(properties, DvbProperty.Frequency)
        );
        Assert.Equal(DvbProperty.Tune, properties.PropertyAt(properties.Count - 1));
    }

    [Fact]
    public void ATypedBroadcastSatelliteTuneReachesTheFrontendWithItsStreamNamed()
    {
        var (calls, clock) = Ready();
        var tune = TuneParams.Bs(15, SyntheticStream);

        using var device = Factory(calls, clock).Create(Satellite(), tune.ToLegacyRequest(), tune);

        var properties = Assert.Single(calls.PropertiesSet);

        Assert.Equal(
            (uint)DeliverySystem.IsdbSatellite.Code,
            ValueOf(properties, DvbProperty.DeliverySystem)
        );
        Assert.Equal(
            (uint)BroadcastStandards.BsCentreKHz(15),
            ValueOf(properties, DvbProperty.Frequency)
        );
        Assert.Equal((uint)SyntheticStream, ValueOf(properties, DvbProperty.StreamId));
        Assert.Equal(DvbProperty.Tune, properties.PropertyAt(properties.Count - 1));
    }

    [Fact]
    public void ATypedCommunicationSatelliteTuneReachesTheFrontendWithoutAStream()
    {
        var (calls, clock) = Ready();
        var tune = TuneParams.Cs110(24);

        using var device = Factory(calls, clock).Create(Satellite(), tune.ToLegacyRequest(), tune);

        var properties = Assert.Single(calls.PropertiesSet);

        Assert.Equal(
            (uint)BroadcastStandards.Cs110CentreKHz(24),
            ValueOf(properties, DvbProperty.Frequency)
        );
        Assert.Equal(-1, IndexOf(properties, DvbProperty.StreamId));
        Assert.Equal(DvbProperty.Tune, properties.PropertyAt(properties.Count - 1));
    }

    [Fact]
    public void ATypedSatelliteTuneLeavesTheAerialUnfedUnlessTheLedgerSaysOtherwise()
    {
        var (calls, clock) = Ready();
        var tune = TuneParams.Cs110(24);

        using var device = Factory(calls, clock).Create(Satellite(), tune.ToLegacyRequest(), tune);

        Assert.Equal(LnbVoltage.Off, Assert.Single(calls.VoltagesSet));
    }

    [Fact]
    public void ATypedSatelliteTuneFeedsTheAerialWhenTheLedgerSaysTo()
    {
        var (calls, clock) = Ready();
        var tune = TuneParams.Cs110(24);

        using var device = Factory(calls, clock)
            .Create(Satellite() with { LnbPower = true }, tune.ToLegacyRequest(), tune);

        Assert.Equal(LnbVoltage.Eighteen, Assert.Single(calls.VoltagesSet));
    }

    [Fact]
    public void ATypedTerrestrialTuneNeverPutsAnythingOnTheAerial()
    {
        var (calls, clock) = Ready();
        var tune = TuneParams.Terrestrial(UnassignedTerrestrialChannel);

        using var device = Factory(calls, clock)
            .Create(Terrestrial(), tune.ToLegacyRequest(), tune);

        Assert.Empty(calls.VoltagesSet);
    }

    [Fact]
    public void TheThreeSystemsDifferOnlyInWhatTheyPutInThePropertyList()
    {
        var (calls, clock) = Ready();
        var factory = Factory(calls, clock);

        using (
            factory.Create(
                Terrestrial(),
                TuneParams.Terrestrial(UnassignedTerrestrialChannel).ToLegacyRequest(),
                TuneParams.Terrestrial(UnassignedTerrestrialChannel)
            )
        )
        { }

        using (
            factory.Create(
                Satellite(),
                TuneParams.Bs(15, SyntheticStream).ToLegacyRequest(),
                TuneParams.Bs(15, SyntheticStream)
            )
        )
        { }

        using (
            factory.Create(
                Satellite(),
                TuneParams.Cs110(24).ToLegacyRequest(),
                TuneParams.Cs110(24)
            )
        )
        { }

        Assert.All(
            calls.PropertiesSet,
            properties =>
            {
                Assert.Equal(DvbProperty.Clear, properties.PropertyAt(0));
                Assert.Equal(DvbProperty.Tune, properties.PropertyAt(properties.Count - 1));
                Assert.True(IndexOf(properties, DvbProperty.DeliverySystem) >= 0);
                Assert.True(IndexOf(properties, DvbProperty.Frequency) >= 0);
            }
        );

        Assert.Equal(3, calls.PropertiesSet.Count);
        Assert.Equal(3, calls.FiltersSet.Count);
        Assert.Equal(3, calls.BufferSizesSet.Count);
        Assert.Equal(
            [
                DvbAccess.Control,
                DvbAccess.Control,
                DvbAccess.Stream,
                DvbAccess.Control,
                DvbAccess.Control,
                DvbAccess.Stream,
                DvbAccess.Control,
                DvbAccess.Control,
                DvbAccess.Stream,
            ],
            calls.Opened.Select(node => node.Access)
        );
    }

    private static bool Accepts(TuneParams tune)
    {
        try
        {
            DvbTuneRequest.Resolve(tune, tune.ToLegacyRequest());

            return true;
        }
        catch (DvbDeviceException)
        {
            return false;
        }
    }

    private static TunerDeviceFactory Factory(
        ScriptedDvbSystemCalls calls,
        ManualTimeProvider clock
    ) =>
        TunerDeviceFactory.Using(
            new DriverConfiguration(null, null, 0, new TunerSettings(TunerBackend.Dvb), null),
            clock,
            calls
        );

    private static DeviceSettings Terrestrial() =>
        new("pt3-0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0");

    private static DeviceSettings Satellite() =>
        new("pt3-2", DeviceKind.Satellite, "/dev/dvb/adapter2/frontend0");

    private static (ScriptedDvbSystemCalls Calls, ManualTimeProvider Clock) Ready()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);

        return (calls, clock);
    }

    private static int IndexOf(DvbPropertyList list, DvbProperty property)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list.PropertyAt(index) == property)
            {
                return index;
            }
        }

        return -1;
    }

    private static uint ValueOf(DvbPropertyList list, DvbProperty property)
    {
        var index = IndexOf(list, property);

        Assert.True(index >= 0);

        return list.DataAt(index);
    }
}
