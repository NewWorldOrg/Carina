using System.Buffers.Binary;

using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed record OpenedNode(string Path, DvbAccess Access, int Descriptor);

public sealed class ScriptedDvbSystemCalls : IDvbSystemCalls
{
    private readonly Dictionary<string, int> refusedOpens = [];
    private readonly Dictionary<DvbProperty, IReadOnlyList<DvbStatisticLayer>> statistics = [];
    private readonly Queue<uint> statusFlags = new();
    private readonly Queue<byte[]> streamed = new();
    private readonly ManualTimeProvider? clock;

    private int nextDescriptor = 3;
    private uint standingStatus;

    public ScriptedDvbSystemCalls(ManualTimeProvider? clock = null)
    {
        this.clock = clock;
    }

    public List<OpenedNode> Opened { get; } = [];

    public List<int> Closed { get; } = [];

    public List<DvbPropertyList> PropertiesSet { get; } = [];

    public List<LnbVoltage> VoltagesSet { get; } = [];

    public List<byte[]> FiltersSet { get; } = [];

    public List<int> BufferSizesSet { get; } = [];

    public int FiltersStopped { get; private set; }

    public IReadOnlyList<DeliverySystem> DeliverySystems { get; set; } = [];

    public string HardwareName { get; set; } = string.Empty;

    public DvbProperty? RefuseProperty { get; set; }

    public int RefusePropertyWith { get; set; } = Errno.NoSuchDevice;

    public int? RefuseStatusWith { get; set; }

    public int? RefuseVoltageWith { get; set; }

    public int? RefuseFilterWith { get; set; }

    public int? RefuseInfoWith { get; set; }

    public int? RefusePropertySetWith { get; set; }

    public Queue<SyscallOutcome> Reads { get; } = new();

    public Queue<SyscallOutcome> Polls { get; } = new();

    public TimeSpan RestedFor { get; private set; }

    public void RefuseToOpen(string path, int error) => refusedOpens[path] = error;

    public void AnswerWith(DvbProperty property, IReadOnlyList<DvbStatisticLayer> layers) =>
        statistics[property] = layers;

    public void ReportStatus(FrontendStatus status) => standingStatus = (uint)status;

    public void ReportStatusesInTurn(params FrontendStatus[] statuses)
    {
        foreach (var status in statuses)
        {
            statusFlags.Enqueue((uint)status);
        }
    }

    public void Deliver(byte[] bytes) => streamed.Enqueue(bytes);

    public SyscallOutcome Open(string path, DvbAccess access)
    {
        if (refusedOpens.TryGetValue(path, out var error))
        {
            return SyscallOutcome.Failed(error);
        }

        var descriptor = nextDescriptor++;
        Opened.Add(new OpenedNode(path, access, descriptor));

        return SyscallOutcome.Ok(descriptor);
    }

    public SyscallOutcome Close(int descriptor)
    {
        Closed.Add(descriptor);

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome SetProperties(int descriptor, byte[] records)
    {
        if (RefusePropertySetWith is { } error)
        {
            return SyscallOutcome.Failed(error);
        }

        PropertiesSet.Add(DvbPropertyList.Over((byte[])records.Clone()));

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome GetProperties(int descriptor, byte[] records)
    {
        var count = records.Length / DvbLayout.PropertyBytes;

        for (var index = 0; index < count; index++)
        {
            var record = index * DvbLayout.PropertyBytes;
            var property = (DvbProperty)
                BinaryPrimitives.ReadUInt32LittleEndian(records.AsSpan(record));

            if (property == RefuseProperty)
            {
                return SyscallOutcome.Failed(RefusePropertyWith);
            }

            if (property is DvbProperty.EnumerateDeliverySystems)
            {
                WriteDeliverySystems(records, record);

                continue;
            }

            if (statistics.TryGetValue(property, out var layers))
            {
                WriteStatistics(records, record, layers);
            }
        }

        return SyscallOutcome.Ok(0);
    }

    public int StatusReads { get; private set; }

    public SyscallOutcome ReadStatus(int descriptor, out uint flags)
    {
        flags = 0;
        StatusReads++;

        if (RefuseStatusWith is { } error)
        {
            return SyscallOutcome.Failed(error);
        }

        flags = statusFlags.Count > 0 ? statusFlags.Dequeue() : standingStatus;

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome ReadFrontendInfo(int descriptor, byte[] block)
    {
        if (RefuseInfoWith is { } error)
        {
            return SyscallOutcome.Failed(error);
        }

        var name = System.Text.Encoding.ASCII.GetBytes(HardwareName);
        name.CopyTo(block, DvbLayout.FrontendNameAt);

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome SetLnbVoltage(int descriptor, LnbVoltage voltage)
    {
        if (RefuseVoltageWith is { } error)
        {
            return SyscallOutcome.Failed(error);
        }

        VoltagesSet.Add(voltage);

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome SetPesFilter(int descriptor, byte[] filter)
    {
        if (RefuseFilterWith is { } error)
        {
            return SyscallOutcome.Failed(error);
        }

        FiltersSet.Add(filter);

        return SyscallOutcome.Ok(0);
    }

    public int? RefuseBufferSizeWith { get; set; }

    public SyscallOutcome SetBufferSize(int descriptor, int bytes)
    {
        if (RefuseBufferSizeWith is { } error)
        {
            return SyscallOutcome.Failed(error);
        }

        BufferSizesSet.Add(bytes);

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome StopFilter(int descriptor)
    {
        FiltersStopped++;

        return SyscallOutcome.Ok(0);
    }

    public SyscallOutcome ReadBytes(int descriptor, byte[] buffer, int count)
    {
        if (Reads.Count > 0)
        {
            return Reads.Dequeue();
        }

        if (streamed.Count is 0)
        {
            return SyscallOutcome.Failed(Errno.WouldBlock);
        }

        var bytes = streamed.Dequeue();
        var taken = Math.Min(count, bytes.Length);
        bytes.AsSpan(0, taken).CopyTo(buffer);

        return SyscallOutcome.Ok(taken);
    }

    public SyscallOutcome WaitForReadable(int descriptor, int timeoutMilliseconds)
    {
        if (Polls.Count > 0)
        {
            return Polls.Dequeue();
        }

        if (streamed.Count > 0)
        {
            return SyscallOutcome.Ok(1);
        }

        clock?.Advance(TimeSpan.FromMilliseconds(timeoutMilliseconds));

        return SyscallOutcome.Ok(0);
    }

    public void Rest(TimeSpan interval, CancellationToken cancellationToken)
    {
        RestedFor += interval;
        clock?.Advance(interval);
    }

    private void WriteDeliverySystems(byte[] records, int record)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(
            records.AsSpan(record + DvbLayout.BufferLengthAt),
            (uint)DeliverySystems.Count
        );

        for (var system = 0; system < DeliverySystems.Count; system++)
        {
            records[record + DvbLayout.BufferDataAt + system] = (byte)
                DeliverySystems[system].Code;
        }
    }

    private static void WriteStatistics(
        byte[] records,
        int record,
        IReadOnlyList<DvbStatisticLayer> layers
    )
    {
        records[record + DvbLayout.StatisticCountAt] = (byte)layers.Count;

        for (var layer = 0; layer < layers.Count; layer++)
        {
            var at = record + DvbLayout.StatisticsAt + (layer * DvbLayout.StatisticBytes);
            records[at] = (byte)layers[layer].Scale;
            BinaryPrimitives.WriteInt64LittleEndian(
                records.AsSpan(at + DvbLayout.StatisticScaleBytes),
                layers[layer].Value
            );
        }
    }
}
