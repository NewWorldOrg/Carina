using System.Buffers.Binary;

namespace Carina.Driver.Tuning.Dvb;

public enum DvbProperty
{
    Unspecified = 0,

    Tune = 1,

    Clear = 2,

    Frequency = 3,

    BandwidthHertz = 5,

    DeliverySystem = 17,

    ApiVersion = 35,

    StreamId = 42,

    EnumerateDeliverySystems = 44,

    CarrierToNoise = 63,

    PostErrorBitCount = 66,

    PostTotalBitCount = 67,
}

public enum StatisticScale
{
    NotAvailable = 0,

    Decibel = 1,

    Relative = 2,

    Counter = 3,
}

public readonly record struct DeliverySystem(int Code)
{
    public static readonly DeliverySystem IsdbTerrestrial = new(8);

    public static readonly DeliverySystem IsdbSatellite = new(9);
}

public readonly record struct DvbStatisticLayer(StatisticScale Scale, long Value);

public readonly record struct DvbPropertySetting(DvbProperty Property, uint Data);

public sealed class DvbPropertyList
{
    private readonly byte[] bytes;

    private DvbPropertyList(int count)
    {
        bytes = new byte[count * DvbLayout.PropertyBytes];
    }

    private DvbPropertyList(byte[] records)
    {
        bytes = records;
    }

    public static DvbPropertyList Over(byte[] records) => new(records);

    public byte[] Bytes => bytes;

    public int Count => bytes.Length / DvbLayout.PropertyBytes;

    public static DvbPropertyList Asking(params DvbProperty[] properties)
    {
        var list = new DvbPropertyList(properties.Length);

        for (var index = 0; index < properties.Length; index++)
        {
            list.WriteCommand(index, properties[index]);
        }

        return list;
    }

    public static DvbPropertyList Setting(IReadOnlyList<DvbPropertySetting> settings)
    {
        var list = new DvbPropertyList(settings.Count);

        for (var index = 0; index < settings.Count; index++)
        {
            list.WriteCommand(index, settings[index].Property);
            BinaryPrimitives.WriteUInt32LittleEndian(
                list.bytes.AsSpan(list.RecordAt(index) + DvbLayout.PropertyPayloadAt),
                settings[index].Data
            );
        }

        return list;
    }

    public DvbProperty PropertyAt(int index) =>
        (DvbProperty)
            BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(RecordAt(index) + DvbLayout.PropertyCommandAt)
            );

    public uint DataAt(int index) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(RecordAt(index) + DvbLayout.PropertyPayloadAt)
        );

    public bool EchoesWhatWasAsked(params DvbProperty[] properties)
    {
        if (properties.Length != Count)
        {
            return false;
        }

        for (var index = 0; index < properties.Length; index++)
        {
            if (PropertyAt(index) != properties[index])
            {
                return false;
            }
        }

        return true;
    }

    public bool TryReadStatisticLayers(int index, out IReadOnlyList<DvbStatisticLayer> layers)
    {
        layers = [];

        if (!Holds(index))
        {
            return false;
        }

        var record = RecordAt(index);
        var count = bytes[record + DvbLayout.StatisticCountAt];

        if (count > DvbLayout.MaxStatisticLayers)
        {
            return false;
        }

        var gathered = new DvbStatisticLayer[count];

        for (var layer = 0; layer < count; layer++)
        {
            var at = record + DvbLayout.StatisticsAt + (layer * DvbLayout.StatisticBytes);
            gathered[layer] = new DvbStatisticLayer(
                (StatisticScale)bytes[at],
                BinaryPrimitives.ReadInt64LittleEndian(
                    bytes.AsSpan(at + DvbLayout.StatisticScaleBytes)
                )
            );
        }

        layers = gathered;

        return true;
    }

    public bool TryReadDeliverySystems(int index, out IReadOnlyList<DeliverySystem> systems)
    {
        systems = [];

        if (!Holds(index))
        {
            return false;
        }

        var record = RecordAt(index);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(record + DvbLayout.BufferLengthAt)
        );

        if (count > DvbLayout.BufferDataBytes)
        {
            return false;
        }

        var gathered = new DeliverySystem[count];

        for (var system = 0; system < count; system++)
        {
            gathered[system] = new DeliverySystem(bytes[record + DvbLayout.BufferDataAt + system]);
        }

        systems = gathered;

        return true;
    }

    private bool Holds(int index) => index >= 0 && index < Count;

    private int RecordAt(int index) => index * DvbLayout.PropertyBytes;

    private void WriteCommand(int index, DvbProperty property) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(RecordAt(index) + DvbLayout.PropertyCommandAt),
            (uint)property
        );
}
