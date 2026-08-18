using System.Text;

namespace Carina.Driver.Tuning.Dvb;

public sealed class DvbFrontend : IDisposable
{
    private readonly IDvbSystemCalls calls;
    private readonly int descriptor;
    private bool closed;

    private DvbFrontend(IDvbSystemCalls calls, string path, int descriptor)
    {
        this.calls = calls;
        this.descriptor = descriptor;
        Path = path;
    }

    public string Path { get; }

    public static DvbFrontend Open(IDvbSystemCalls calls, string path, DvbAccess access)
    {
        SyscallOutcome opened = calls.Open(path, access);

        if (opened.Refused)
        {
            throw DvbFailure.AtDevice(
                path,
                "opening the frontend",
                opened.Error,
                opened.Error is Errno.Busy
                    ? "Another process is already holding this tuner."
                    : "The tuner cannot be used until this device node exists and this process is allowed to open it."
            );
        }

        return new DvbFrontend(calls, path, opened.Value);
    }

    public FrontendStatus Status()
    {
        SyscallOutcome read = calls.ReadStatus(descriptor, out uint flags);

        if (read.Refused)
        {
            throw DvbFailure.AtDevice(
                Path,
                "reading the frontend status",
                read.Error,
                "Without a status the driver cannot tell a locked tuner from an unlocked one, so it will not report either."
            );
        }

        return (FrontendStatus)flags;
    }

    public bool TryStatus(out FrontendStatus status)
    {
        SyscallOutcome read = calls.ReadStatus(descriptor, out uint flags);
        status = (FrontendStatus)flags;

        return !read.Refused;
    }

    public void Tune(DvbChannel channel)
    {
        DvbPropertyList properties = DvbTuning.PropertiesFor(channel);
        SyscallOutcome set = calls.SetProperties(descriptor, properties.Bytes);

        if (set.Refused)
        {
            throw DvbFailure.AtDevice(
                Path,
                $"tuning to {channel}",
                set.Error,
                "The frontend kept whatever it was tuned to before, so nothing here is receiving that channel."
            );
        }
    }

    public void SetLnbVoltage(LnbVoltage voltage)
    {
        SyscallOutcome set = calls.SetLnbVoltage(descriptor, voltage);

        if (set.Refused)
        {
            throw DvbFailure.AtDevice(
                Path,
                $"setting the aerial supply to {voltage}",
                set.Error,
                "The driver will not tune a satellite channel while it cannot tell whether the aerial is being fed."
            );
        }
    }

    public bool WaitForLock(
        TimeProvider time,
        TimeSpan patience,
        TimeSpan interval,
        CancellationToken cancellationToken,
        out FrontendStatus lastSeen
    )
    {
        DateTimeOffset deadline = time.GetUtcNow() + patience;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastSeen = Status();

            if (lastSeen.HasFlag(FrontendStatus.Lock))
            {
                return true;
            }

            if (time.GetUtcNow() >= deadline)
            {
                return false;
            }

            calls.Rest(interval, cancellationToken);
        }
    }

    public SignalQuality Quality()
    {
        FrontendStatus before = Status();
        DvbProperty[] asked = new[]
        {
            DvbProperty.CarrierToNoise,
            DvbProperty.PostErrorBitCount,
            DvbProperty.PostTotalBitCount,
        };
        var answer = DvbPropertyList.Asking(asked);
        SyscallOutcome read = calls.GetProperties(descriptor, answer.Bytes);

        if (read.Refused)
        {
            throw DvbFailure.AtDevice(
                Path,
                "reading the signal statistics",
                read.Error,
                "The driver reports no quality rather than a number it cannot stand behind."
            );
        }

        if (!answer.EchoesWhatWasAsked(asked))
        {
            throw DvbFailure.Refused(
                $"{Path}: the frontend answered a statistics request with different properties than were asked for, so the layout this driver uses does not match this kernel and none of the values can be trusted."
            );
        }

        if (
            !answer.TryReadStatisticLayers(0, out IReadOnlyList<DvbStatisticLayer>? carrier)
            || !answer.TryReadStatisticLayers(1, out IReadOnlyList<DvbStatisticLayer>? errorBits)
            || !answer.TryReadStatisticLayers(2, out IReadOnlyList<DvbStatisticLayer>? totalBits)
        )
        {
            throw DvbFailure.Refused(
                $"{Path}: the frontend reported more statistic layers than the kernel structure can hold, so the layout this driver uses does not match this kernel and none of the values can be trusted."
            );
        }

        var locked = new LockWindow(before, Status());

        return new SignalQuality(
            locked,
            SignalQualityReading.CarrierToNoiseFrom(locked, carrier),
            SignalQualityReading.PostViterbiFrom(locked, errorBits, totalBits)
        );
    }

    public bool TryReadDeliverySystems(
        out IReadOnlyList<DeliverySystem> systems,
        out string problem
    )
    {
        systems = [];
        problem = string.Empty;

        var answer = DvbPropertyList.Asking(DvbProperty.EnumerateDeliverySystems);
        SyscallOutcome read = calls.GetProperties(descriptor, answer.Bytes);

        if (read.Refused)
        {
            problem =
                $"the frontend would not enumerate its delivery systems ({DvbFailure.Describe(read.Error)})";

            return false;
        }

        if (!answer.EchoesWhatWasAsked(DvbProperty.EnumerateDeliverySystems))
        {
            problem =
                "the frontend answered the delivery system question with a different property";

            return false;
        }

        if (!answer.TryReadDeliverySystems(0, out systems))
        {
            problem = "the frontend reported more delivery systems than its buffer can hold";

            return false;
        }

        if (systems.Count is 0)
        {
            problem = "the frontend enumerated no delivery systems at all";

            return false;
        }

        return true;
    }

    public bool TryReadHardwareName(out string name, out string problem)
    {
        name = string.Empty;
        problem = string.Empty;

        byte[] block = new byte[DvbLayout.FrontendInfoBytes];
        SyscallOutcome read = calls.ReadFrontendInfo(descriptor, block);

        if (read.Refused)
        {
            problem =
                $"the frontend would not describe itself ({DvbFailure.Describe(read.Error)})";

            return false;
        }

        name = ReadName(block);

        if (name.Length is 0)
        {
            problem = "the frontend described itself with an empty name";

            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        calls.Close(descriptor);
    }

    private static string ReadName(byte[] block)
    {
        Span<byte> name = block.AsSpan(DvbLayout.FrontendNameAt, DvbLayout.FrontendNameBytes);
        int end = name.IndexOf((byte)0);

        return Encoding.ASCII.GetString(end < 0 ? name : name[..end]).Trim();
    }
}
