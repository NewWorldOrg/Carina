using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbTuningTests
{
    private const int SyntheticStream = 50_001;


    [Fact]
    public void EveryTunePropertyListEndsByAskingTheFrontendToTune()
    {
        DvbChannel[] channels =
        [
            DvbChannel.Terrestrial(27),
            DvbChannel.BroadcastSatellite(1, SyntheticStream),
            DvbChannel.BroadcastSatellite(9, SyntheticStream),
            DvbChannel.CommunicationSatellite(24),
        ];

        Assert.All(
            channels,
            channel =>
            {
                var list = DvbTuning.PropertiesFor(channel);

                Assert.Equal(DvbProperty.Tune, list.PropertyAt(list.Count - 1));
            }
        );
    }

    [Fact]
    public void EveryTunePropertyListStartsByClearingWhatTheLastTuneLeftBehind()
    {
        DvbChannel[] channels =
        [
            DvbChannel.Terrestrial(27),
            DvbChannel.BroadcastSatellite(1, SyntheticStream),
            DvbChannel.CommunicationSatellite(24),
        ];

        Assert.All(
            channels,
            channel => Assert.Equal(DvbProperty.Clear, DvbTuning.PropertiesFor(channel).PropertyAt(0))
        );
    }

    [Fact]
    public void ATerrestrialTuneNamesTheTerrestrialSystemItsFrequencyInHertzAndItsBandwidth()
    {
        var list = DvbTuning.PropertiesFor(DvbChannel.Terrestrial(27));

        Assert.Equal(
            (uint)DeliverySystem.IsdbTerrestrial.Code,
            ValueOf(list, DvbProperty.DeliverySystem)
        );
        Assert.Equal(557_142_857u, ValueOf(list, DvbProperty.Frequency));
        Assert.Equal(6_000_000u, ValueOf(list, DvbProperty.BandwidthHertz));
    }

    [Fact]
    public void ATerrestrialTuneNeverNamesAStream()
    {
        Assert.False(Names(DvbTuning.PropertiesFor(DvbChannel.Terrestrial(27)), DvbProperty.StreamId));
    }

    [Fact]
    public void ABroadcastSatelliteTuneNamesTheSatelliteSystemAndItsFrequencyInKilohertz()
    {
        var list = DvbTuning.PropertiesFor(DvbChannel.BroadcastSatellite(15, SyntheticStream));

        Assert.Equal(
            (uint)DeliverySystem.IsdbSatellite.Code,
            ValueOf(list, DvbProperty.DeliverySystem)
        );
        Assert.Equal(1_318_000u, ValueOf(list, DvbProperty.Frequency));
    }

    [Fact]
    public void ABroadcastSatelliteTuneNamesTheStreamWhenOneWasChosen()
    {
        var list = DvbTuning.PropertiesFor(DvbChannel.BroadcastSatellite(15, SyntheticStream));

        Assert.Equal((uint)SyntheticStream, ValueOf(list, DvbProperty.StreamId));
    }

    [Fact]
    public void EveryBroadcastSatelliteTuneNamesTheStreamSoASharedSlotCannotAnswerForAnother()
    {
        int[] slots = [1, 9, 15, 23];

        Assert.All(
            slots,
            slot =>
                Assert.Equal(
                    (uint)SyntheticStream,
                    ValueOf(
                        DvbTuning.PropertiesFor(
                            DvbChannel.BroadcastSatellite(slot, SyntheticStream)
                        ),
                        DvbProperty.StreamId
                    )
                )
        );
    }

    [Fact]
    public void ACommunicationSatelliteTuneNeverNamesAStreamBecauseOneSlotCarriesOneStream()
    {
        var list = DvbTuning.PropertiesFor(DvbChannel.CommunicationSatellite(24));

        Assert.False(Names(list, DvbProperty.StreamId));
        Assert.Equal(2_053_000u, ValueOf(list, DvbProperty.Frequency));
    }

    [Fact]
    public void ASatelliteTuneCarriesNoTerrestrialBandwidth()
    {
        Assert.False(
            Names(
                DvbTuning.PropertiesFor(DvbChannel.BroadcastSatellite(1, SyntheticStream)),
                DvbProperty.BandwidthHertz
            )
        );
    }

    [Fact]
    public void TheDeliverySystemIsNamedBeforeTheFrequencyItAppliesTo()
    {
        var list = DvbTuning.PropertiesFor(DvbChannel.Terrestrial(27));

        Assert.True(IndexOf(list, DvbProperty.DeliverySystem) < IndexOf(list, DvbProperty.Frequency));
    }

    [Fact]
    public void ClearingFirstMeansAStreamFromAnEarlierTuneCannotSurviveIntoTheNext()
    {
        var satellite = DvbTuning.PropertiesFor(DvbChannel.BroadcastSatellite(15, SyntheticStream));
        var afterwards = DvbTuning.PropertiesFor(DvbChannel.CommunicationSatellite(24));

        Assert.True(Names(satellite, DvbProperty.StreamId));
        Assert.Equal(DvbProperty.Clear, afterwards.PropertyAt(0));
        Assert.False(Names(afterwards, DvbProperty.StreamId));
    }

    private static bool Names(DvbPropertyList list, DvbProperty property) =>
        IndexOf(list, property) >= 0;

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
