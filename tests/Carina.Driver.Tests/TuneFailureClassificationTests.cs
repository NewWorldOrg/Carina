using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tests;

public sealed class TuneFailureClassificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Deadlock = TimeSpan.FromSeconds(30);

    private readonly ManualTimeProvider clock = new(Start);

    [Fact]
    public void AFrontendThatDidNotLockIsAnsweredAsThatChannelsOutcome()
    {
        var manager = Manager(new ThrowingDeviceFactory(() => DvbFailure.NoLock(
            "/dev/dvb/adapter0/frontend0: the frontend did not lock within 5 seconds,"
            + " and the last status it reported while waiting was None."
        )));

        var start = manager.Begin(Request("scan-14", 14));

        Assert.Equal(SessionRefusal.NoLock, start.Refusal);
        Assert.Contains("did not lock", start.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeviceThatCannotBeOpenedIsStillAnsweredAsUnavailable()
    {
        var manager = Manager(new ThrowingDeviceFactory(() => DvbFailure.AtDevice(
            "/dev/dvb/adapter0/frontend0",
            "opening the frontend",
            Errno.Busy,
            "Another process holds the frontend."
        )));

        var start = manager.Begin(Request("scan-14", 14));

        Assert.Equal(SessionRefusal.DeviceUnavailable, start.Refusal);
    }

    [Fact]
    public void AChannelThatDeliveredNoBytesEndsItsSessionWithoutCondemningTheTuner()
    {
        var devices = new ConcurrentQueue<ITunerDevice>(
            [new SilentAfterLockDevice(), new ScriptedTunerDevice()]
        );
        var manager = Manager(new QueuedDeviceFactory(devices));

        var first = manager.Begin(Request("scan-14", 14));

        Assert.True(first.TryGetSession(out var silent));

        silent.WaitForEnd(Deadlock);

        Assert.Equal(SessionState.Failed, silent.State);
        Assert.NotEqual(SessionStopReason.DeviceFailed, silent.StopReason);
        Assert.False(manager.IsFaulted("adapter0", out _));

        var second = manager.Begin(Request("scan-15", 15));

        Assert.Equal(SessionRefusal.None, second.Refusal);
        Assert.True(second.TryGetSession(out var next));

        next.Stop();
        next.WaitForEnd(Deadlock);
    }

    private TunerSessionManager Manager(ITunerDeviceFactory factory) =>
        new(
            new DriverConfiguration(
                "/run/carina/driver.sock",
                [],
                6,
                new TunerSettings(TunerBackend.Fake),
                [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
            ),
            factory,
            clock,
            NullLogger<TunerSessionManager>.Instance
        );

    private static StartSessionRequest Request(string sessionId, int channel) =>
        new()
        {
            SessionId = SessionId.Parse(sessionId),
            Purpose = SessionPurpose.Scan,
            Tuning = new TuningRequest(TunerKind.Terrestrial, channel, 50001),
            EndsAt = Start.AddHours(1),
        };

    private sealed class ThrowingDeviceFactory(Func<Exception> failure) : ITunerDeviceFactory
    {
        public ITunerDevice Create(
            DeviceSettings device,
            TuningRequest tuning,
            TuneParams? tune
        ) => throw failure();
    }

    private sealed class QueuedDeviceFactory(ConcurrentQueue<ITunerDevice> devices)
        : ITunerDeviceFactory
    {
        public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
        {
            Assert.True(devices.TryDequeue(out var next), "The test scripted too few devices.");

            return next;
        }
    }

    private sealed class SilentAfterLockDevice : ITunerDevice
    {
        public long Overflows => 0;

        public bool Disposed { get; private set; }

        public byte[] Read(int count, CancellationToken cancellationToken) =>
            throw DvbFailure.LockedWithoutData(
                "/dev/dvb/adapter0/dvr0: no transport stream bytes arrived within 5 seconds,"
                + " and the frontend reports it is still locked."
            );

        public void Dispose() => Disposed = true;
    }
}
