namespace Carina.Contracts;

public static class BroadcastStandards
{
    public const int TerrestrialFirstChannel = 13;

    public const int TerrestrialLastChannel = 62;

    public const long TerrestrialFirstChannelCentreHz = 473_142_857;

    public const long TerrestrialChannelSpacingHz = 6_000_000;

    public const int BsFirstChannel = 1;

    public const int BsLastChannel = 23;

    public const long BsFirstChannelCentreKHz = 1_049_480;

    public const long BsSlotSpacingKHz = 19_180;

    public const int Cs110FirstChannel = 2;

    public const int Cs110LastChannel = 24;

    public const long Cs110FirstChannelCentreKHz = 1_613_000;

    public const long Cs110SlotSpacingKHz = 20_000;

    public const int MinTransportStreamId = 0;

    public const int MaxTransportStreamId = 65535;

    public static readonly IReadOnlyList<int> BsChannelsWithoutDemodulation = [7, 17];

    public static bool IsTerrestrialChannel(int physicalChannel) =>
        physicalChannel is >= TerrestrialFirstChannel and <= TerrestrialLastChannel;

    public static bool IsBsChannel(int bsChannel) =>
        bsChannel is >= BsFirstChannel and <= BsLastChannel
        && int.IsOddInteger(bsChannel)
        && !BsChannelsWithoutDemodulation.Contains(bsChannel);

    public static bool IsCs110Channel(int csChannel) =>
        csChannel is >= Cs110FirstChannel and <= Cs110LastChannel && int.IsEvenInteger(csChannel);

    public static bool IsTransportStreamId(int tsid) =>
        tsid is >= MinTransportStreamId and <= MaxTransportStreamId;

    public static long TerrestrialCentreHz(int physicalChannel)
    {
        if (!IsTerrestrialChannel(physicalChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalChannel),
                physicalChannel,
                $"A terrestrial channel is {TerrestrialFirstChannel} to {TerrestrialLastChannel}."
            );
        }

        return TerrestrialFirstChannelCentreHz
            + ((physicalChannel - TerrestrialFirstChannel) * TerrestrialChannelSpacingHz);
    }

    public static long BsCentreKHz(int bsChannel)
    {
        if (!IsBsChannel(bsChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bsChannel),
                bsChannel,
                $"A BS slot is an odd {BsFirstChannel} to {BsLastChannel}, less {string.Join(" and ", BsChannelsWithoutDemodulation)}."
            );
        }

        return BsFirstChannelCentreKHz + ((bsChannel - BsFirstChannel) * BsSlotSpacingKHz);
    }

    public static long Cs110CentreKHz(int csChannel)
    {
        if (!IsCs110Channel(csChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(csChannel),
                csChannel,
                $"A CS110 slot is an even {Cs110FirstChannel} to {Cs110LastChannel}."
            );
        }

        return Cs110FirstChannelCentreKHz + ((csChannel - Cs110FirstChannel) * Cs110SlotSpacingKHz);
    }
}
