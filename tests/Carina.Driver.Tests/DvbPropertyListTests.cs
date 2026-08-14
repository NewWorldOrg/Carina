using System.Buffers.Binary;

using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DvbPropertyListTests
{
    [Fact]
    public void AQuestionCarriesOneRecordPerPropertyAsked()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise, DvbProperty.ApiVersion);

        Assert.Equal(2, list.Count);
        Assert.Equal(2 * DvbLayout.PropertyBytes, list.Bytes.Length);
        Assert.Equal(DvbProperty.CarrierToNoise, list.PropertyAt(0));
        Assert.Equal(DvbProperty.ApiVersion, list.PropertyAt(1));
    }

    [Fact]
    public void ACommandNumberIsWrittenAtTheHeadOfItsRecord()
    {
        var list = DvbPropertyList.Setting([new DvbPropertySetting(DvbProperty.Frequency, 473_142_857)]);

        Assert.Equal(
            (uint)DvbProperty.Frequency,
            BinaryPrimitives.ReadUInt32LittleEndian(list.Bytes.AsSpan(DvbLayout.PropertyCommandAt))
        );
    }

    [Fact]
    public void AScalarValueIsWrittenAtTheHeadOfThePayload()
    {
        var list = DvbPropertyList.Setting([new DvbPropertySetting(DvbProperty.Frequency, 473_142_857)]);

        Assert.Equal(
            473_142_857u,
            BinaryPrimitives.ReadUInt32LittleEndian(list.Bytes.AsSpan(DvbLayout.PropertyPayloadAt))
        );
        Assert.Equal(473_142_857u, list.DataAt(0));
    }

    [Fact]
    public void TheRecordsOfAListSitBackToBackWithNoPaddingBetween()
    {
        var list = DvbPropertyList.Setting(
            [
                new DvbPropertySetting(DvbProperty.DeliverySystem, 8),
                new DvbPropertySetting(DvbProperty.Frequency, 500),
                new DvbPropertySetting(DvbProperty.Tune, 0),
            ]
        );

        Assert.Equal(
            (uint)DvbProperty.Frequency,
            BinaryPrimitives.ReadUInt32LittleEndian(list.Bytes.AsSpan(DvbLayout.PropertyBytes))
        );
        Assert.Equal(
            (uint)DvbProperty.Tune,
            BinaryPrimitives.ReadUInt32LittleEndian(list.Bytes.AsSpan(2 * DvbLayout.PropertyBytes))
        );
    }

    [Fact]
    public void StatisticLayersAreReadBackAsScaleAndValuePairs()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise);
        FillStatistics(list, 0, [(StatisticScale.Decibel, 12_345L), (StatisticScale.Decibel, -1_000L)]);

        Assert.True(list.TryReadStatisticLayers(0, out var layers));
        Assert.Equal(2, layers.Count);
        Assert.Equal(new DvbStatisticLayer(StatisticScale.Decibel, 12_345L), layers[0]);
        Assert.Equal(new DvbStatisticLayer(StatisticScale.Decibel, -1_000L), layers[1]);
    }

    [Fact]
    public void AStatisticTheTunerDoesNotImplementReadsBackAsNoLayersAtAll()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise);

        Assert.True(list.TryReadStatisticLayers(0, out var layers));
        Assert.Empty(layers);
    }

    [Fact]
    public void AStatisticCountBeyondFourIsRefusedRatherThanTruncated()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise);
        list.Bytes[DvbLayout.StatisticCountAt] = 5;

        Assert.False(list.TryReadStatisticLayers(0, out var layers));
        Assert.Empty(layers);
    }

    [Fact]
    public void DeliverySystemsAreReadBackFromTheBufferMember()
    {
        var list = DvbPropertyList.Asking(DvbProperty.EnumerateDeliverySystems);
        FillDeliverySystems(list, 0, [8, 9]);

        Assert.True(list.TryReadDeliverySystems(0, out var systems));
        Assert.Equal([DeliverySystem.IsdbTerrestrial, DeliverySystem.IsdbSatellite], systems);
    }

    [Fact]
    public void ADeliverySystemCountBeyondTheBufferIsRefusedRatherThanTruncated()
    {
        var list = DvbPropertyList.Asking(DvbProperty.EnumerateDeliverySystems);
        BinaryPrimitives.WriteUInt32LittleEndian(
            list.Bytes.AsSpan(DvbLayout.BufferLengthAt),
            (uint)(DvbLayout.BufferDataBytes + 1)
        );

        Assert.False(list.TryReadDeliverySystems(0, out var systems));
        Assert.Empty(systems);
    }

    [Fact]
    public void AListThatCameBackNamingADifferentPropertyIsNotTheAnswerWeAskedFor()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise);
        BinaryPrimitives.WriteUInt32LittleEndian(
            list.Bytes.AsSpan(DvbLayout.PropertyCommandAt),
            (uint)DvbProperty.PostErrorBitCount
        );

        Assert.False(list.EchoesWhatWasAsked(DvbProperty.CarrierToNoise));
    }

    [Fact]
    public void AListThatCameBackNamingTheSamePropertiesIsTheAnswerWeAskedFor()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise, DvbProperty.PostTotalBitCount);

        Assert.True(
            list.EchoesWhatWasAsked(DvbProperty.CarrierToNoise, DvbProperty.PostTotalBitCount)
        );
    }

    [Fact]
    public void ReachingPastTheEndOfTheListIsRefused()
    {
        var list = DvbPropertyList.Asking(DvbProperty.CarrierToNoise);

        Assert.False(list.TryReadStatisticLayers(1, out _));
        Assert.False(list.TryReadDeliverySystems(1, out _));
    }

    private static void FillStatistics(
        DvbPropertyList list,
        int index,
        IReadOnlyList<(StatisticScale Scale, long Value)> layers
    )
    {
        var record = index * DvbLayout.PropertyBytes;
        list.Bytes[record + DvbLayout.StatisticCountAt] = (byte)layers.Count;

        for (var layer = 0; layer < layers.Count; layer++)
        {
            var at = record + DvbLayout.StatisticsAt + (layer * DvbLayout.StatisticBytes);
            list.Bytes[at] = (byte)layers[layer].Scale;
            BinaryPrimitives.WriteInt64LittleEndian(
                list.Bytes.AsSpan(at + DvbLayout.StatisticScaleBytes),
                layers[layer].Value
            );
        }
    }

    private static void FillDeliverySystems(DvbPropertyList list, int index, IReadOnlyList<byte> codes)
    {
        var record = index * DvbLayout.PropertyBytes;
        BinaryPrimitives.WriteUInt32LittleEndian(
            list.Bytes.AsSpan(record + DvbLayout.BufferLengthAt),
            (uint)codes.Count
        );

        for (var code = 0; code < codes.Count; code++)
        {
            list.Bytes[record + DvbLayout.BufferDataAt + code] = codes[code];
        }
    }
}
