using Carina.Driver.Configuration;

namespace Carina.Driver.Tuning.Dvb;

public sealed record DvbTunerSettings(
    TimeSpan LockPatience,
    TimeSpan RetryInterval,
    TimeSpan BytePatience,
    int DemuxBufferBytes
)
{
    public static readonly DvbTunerSettings Default = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(5),
        16 * 1024 * 1024
    );
}

public static class LnbPower
{
    public static LnbVoltage For(DeviceKind kind, bool enabledInTheLedger) =>
        kind is DeviceKind.Satellite && enabledInTheLedger
            ? LnbVoltage.Eighteen
            : LnbVoltage.Off;
}

public sealed class DvbTunerDevice : ITunerDevice
{
    private readonly IDvbSystemCalls calls;
    private readonly TimeProvider time;
    private readonly DvbDevicePaths paths;
    private readonly DvbTunerSettings settings;
    private readonly DvbFrontend frontend;
    private readonly int demux;
    private readonly int dvr;

    private long overflows;
    private bool closed;

    private DvbTunerDevice(
        IDvbSystemCalls calls,
        TimeProvider time,
        DvbDevicePaths paths,
        DvbTunerSettings settings,
        DvbFrontend frontend,
        int demux,
        int dvr
    )
    {
        this.calls = calls;
        this.time = time;
        this.paths = paths;
        this.settings = settings;
        this.frontend = frontend;
        this.demux = demux;
        this.dvr = dvr;
    }

    public long Overflows => Interlocked.Read(ref overflows);

    public static DvbTunerDevice Open(
        IDvbSystemCalls calls,
        TimeProvider time,
        DvbDevicePaths paths,
        DvbChannel channel,
        LnbVoltage voltage,
        DvbTunerSettings settings,
        CancellationToken cancellationToken
    )
    {
        var frontend = DvbFrontend.Open(calls, paths.Frontend, DvbAccess.Control);
        var demux = -1;
        var dvr = -1;

        try
        {
            if (channel.NeedsSatelliteAerial)
            {
                frontend.SetLnbVoltage(voltage);
            }

            frontend.Tune(channel);

            if (
                !frontend.WaitForLock(
                    time,
                    settings.LockPatience,
                    settings.RetryInterval,
                    cancellationToken,
                    out var lastSeen
                )
            )
            {
                throw DvbFailure.NoLock(
                    $"{paths.Frontend}: the frontend did not lock onto {channel} within {settings.LockPatience.TotalSeconds:0.#} seconds, and the last status it reported while waiting was {lastSeen}. Nothing was received, so no bytes will follow."
                );
            }

            demux = OpenNode(calls, paths.Demux, DvbAccess.Control, "the demux");
            SetBufferSize(calls, paths.Demux, demux, settings.DemuxBufferBytes);
            StartFilter(calls, paths.Demux, demux);
            dvr = OpenNode(calls, paths.Dvr, DvbAccess.Stream, "the transport stream reader");

            return new DvbTunerDevice(calls, time, paths, settings, frontend, demux, dvr);
        }
        catch
        {
            CloseQuietly(calls, dvr);
            CloseQuietly(calls, demux);
            frontend.Dispose();

            throw;
        }
    }

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var deadline = time.GetUtcNow() + settings.BytePatience;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ready = calls.WaitForReadable(
                dvr,
                (int)settings.BytePatience.TotalMilliseconds
            );

            if (ready.Refused && ready.Error is not Errno.Interrupted)
            {
                throw DvbFailure.AtDevice(
                    paths.Dvr,
                    "waiting for transport stream bytes",
                    ready.Error,
                    "The session cannot continue without knowing whether the tuner is still delivering."
                );
            }

            if (!ready.Refused && ready.Value > 0)
            {
                var read = calls.ReadBytes(dvr, buffer, count);

                if (!read.Refused)
                {
                    if (read.Value is 0)
                    {
                        return [];
                    }

                    return read.Value == count ? buffer : buffer[..read.Value];
                }

                if (read.Error is Errno.Overflowed)
                {
                    Interlocked.Increment(ref overflows);
                }
                else if (read.Error is not (Errno.WouldBlock or Errno.Interrupted))
                {
                    throw DvbFailure.AtDevice(
                        paths.Dvr,
                        "reading transport stream bytes",
                        read.Error,
                        "The session cannot continue on a reader that has stopped answering."
                    );
                }
                else
                {
                    calls.Rest(settings.RetryInterval, cancellationToken);
                }
            }

            if (time.GetUtcNow() >= deadline)
            {
                throw NothingArrived();
            }
        }
    }

    private DvbDeviceException NothingArrived()
    {
        var waited = $"{settings.BytePatience.TotalSeconds:0.#} seconds";

        if (!frontend.TryStatus(out var status))
        {
            return DvbFailure.Refused(
                $"{paths.Dvr}: no transport stream bytes arrived within {waited}, and the frontend would not say whether it is still locked. This is left unclassified rather than recorded as a tuner that was locked and delivering nothing."
            );
        }

        if (!status.HasFlag(FrontendStatus.Lock))
        {
            return DvbFailure.NoLock(
                $"{paths.Dvr}: no transport stream bytes arrived within {waited}, and the frontend is no longer locked; its status is now {status}."
            );
        }

        return DvbFailure.LockedWithoutData(
            $"{paths.Dvr}: the frontend is still locked ({status}) and no transport stream bytes arrived within {waited}. The tuner is synchronised and the demux is delivering nothing, which is a different fault from failing to lock."
        );
    }

    public void Dispose()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        CloseQuietly(calls, dvr);
        calls.StopFilter(demux);
        CloseQuietly(calls, demux);
        frontend.Dispose();
    }

    private static int OpenNode(
        IDvbSystemCalls calls,
        string path,
        DvbAccess access,
        string what
    )
    {
        var opened = calls.Open(path, access);

        if (opened.Refused)
        {
            throw DvbFailure.AtDevice(
                path,
                $"opening {what}",
                opened.Error,
                "The frontend locked, but the transport stream cannot be taken off this adapter."
            );
        }

        return opened.Value;
    }

    private static void SetBufferSize(
        IDvbSystemCalls calls,
        string path,
        int descriptor,
        int bytes
    )
    {
        var sized = calls.SetBufferSize(descriptor, bytes);

        if (sized.Refused)
        {
            throw DvbFailure.AtDevice(
                path,
                $"asking for a {bytes} byte ring buffer",
                sized.Error,
                "The default buffer is small enough that a late reader loses bytes silently, so the driver will not proceed without the size it asked for."
            );
        }
    }

    private static void StartFilter(IDvbSystemCalls calls, string path, int descriptor)
    {
        var filtered = calls.SetPesFilter(descriptor, DemuxFilter.EverythingFromTheFrontend());

        if (filtered.Refused)
        {
            throw DvbFailure.AtDevice(
                path,
                "routing every packet to the transport stream reader",
                filtered.Error,
                "Without the filter the reader would block forever on an adapter that is otherwise working."
            );
        }
    }

    private static void CloseQuietly(IDvbSystemCalls calls, int descriptor)
    {
        if (descriptor >= 0)
        {
            calls.Close(descriptor);
        }
    }
}
