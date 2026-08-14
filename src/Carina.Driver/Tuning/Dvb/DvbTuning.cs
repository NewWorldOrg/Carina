namespace Carina.Driver.Tuning.Dvb;

public static class DvbTuning
{
    public static DvbPropertyList PropertiesFor(DvbChannel channel)
    {
        var settings = new List<DvbPropertySetting>
        {
            new(DvbProperty.Clear, 0),
        };

        switch (channel)
        {
            case TerrestrialChannel terrestrial:
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.DeliverySystem,
                        (uint)DeliverySystem.IsdbTerrestrial.Code
                    )
                );
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.Frequency,
                        DvbFrequency.TerrestrialHertz(terrestrial.PhysicalChannel)
                    )
                );
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.BandwidthHertz,
                        DvbFrequency.TerrestrialBandwidthHertz
                    )
                );

                break;

            case BroadcastSatelliteChannel broadcast:
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.DeliverySystem,
                        (uint)DeliverySystem.IsdbSatellite.Code
                    )
                );
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.Frequency,
                        DvbFrequency.BroadcastSatelliteKilohertz(broadcast.Slot)
                    )
                );

                if (broadcast.TransportStreamId is { } stream)
                {
                    settings.Add(new DvbPropertySetting(DvbProperty.StreamId, (uint)stream));
                }

                break;

            case CommunicationSatelliteChannel communication:
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.DeliverySystem,
                        (uint)DeliverySystem.IsdbSatellite.Code
                    )
                );
                settings.Add(
                    new DvbPropertySetting(
                        DvbProperty.Frequency,
                        DvbFrequency.CommunicationSatelliteKilohertz(communication.Slot)
                    )
                );

                break;

            default:
                throw DvbFailure.Refused(
                    $"channel: '{channel.GetType().Name}' is not a channel shape this driver knows how to turn into a property list, so it will not guess."
                );
        }

        settings.Add(new DvbPropertySetting(DvbProperty.Tune, 0));

        return DvbPropertyList.Setting(settings);
    }
}
