namespace Carina.Domain.Channels;

public sealed record TuningParameters
{
    public const int TerrestrialFirstChannel = 13;
    public const int TerrestrialLastChannel = 62;

    public const int BsFirstChannel = 1;
    public const int BsLastChannel = 23;

    public const int Cs110FirstChannel = 2;
    public const int Cs110LastChannel = 24;

    public static readonly IReadOnlyList<int> BsChannelsWithoutDemodulation = [7, 17];

    private TuningParameters(TuneSystem system, int physicalChannel, TransportStreamId? transportStreamId)
    {
        System = system;
        PhysicalChannel = physicalChannel;
        TransportStreamId = transportStreamId;
    }

    public TuneSystem System { get; }

    public int PhysicalChannel { get; }

    public TransportStreamId? TransportStreamId { get; }

    public static TuningParameters Terrestrial(int physicalChannel)
    {
        if (physicalChannel is < TerrestrialFirstChannel or > TerrestrialLastChannel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalChannel),
                physicalChannel,
                $"A terrestrial channel is {TerrestrialFirstChannel} to {TerrestrialLastChannel}.");
        }

        return new TuningParameters(TuneSystem.IsdbT, physicalChannel, null);
    }

    public static TuningParameters Bs(int bsChannel, TransportStreamId transportStreamId)
    {
        ArgumentNullException.ThrowIfNull(transportStreamId);

        if (!IsBsChannel(bsChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bsChannel),
                bsChannel,
                $"A BS slot is an odd {BsFirstChannel} to {BsLastChannel}, less {string.Join(" and ", BsChannelsWithoutDemodulation)}.");
        }

        return new TuningParameters(TuneSystem.IsdbSBs, bsChannel, transportStreamId);
    }

    public static TuningParameters Cs110(int csChannel)
    {
        if (!IsCs110Channel(csChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(csChannel),
                csChannel,
                $"A CS110 slot is an even {Cs110FirstChannel} to {Cs110LastChannel}.");
        }

        return new TuningParameters(TuneSystem.IsdbSCs110, csChannel, null);
    }

    public static bool IsBsChannel(int bsChannel)
        => bsChannel is >= BsFirstChannel and <= BsLastChannel
           && int.IsOddInteger(bsChannel)
           && !BsChannelsWithoutDemodulation.Contains(bsChannel);

    public static bool IsCs110Channel(int csChannel)
        => csChannel is >= Cs110FirstChannel and <= Cs110LastChannel && int.IsEvenInteger(csChannel);
}
