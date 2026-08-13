using Carina.Contracts;
using Carina.Driver.Diagnostics;
using Carina.Driver.Events;

namespace Carina.Driver.Tests;

public sealed class DiagnosticsStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AReportIsReadableWithItsReasonItsSubjectAndItsClock()
    {
        var clock = new ManualTimeProvider(Start);
        var store = new DiagnosticsStore(clock);

        store.Report(
            DiagnosticReason.RecordingWriteFailed,
            "No space left on device",
            "adapter0",
            SessionId.Parse("s-1")
        );
        clock.Advance(TimeSpan.FromMinutes(1));
        store.Report(DiagnosticReason.DeviceFaulted, "the device stopped answering", "adapter1");

        var entries = store.Snapshot();

        Assert.Equal(2, entries.Count);
        Assert.Equal(DiagnosticReason.RecordingWriteFailed, entries[0].Reason);
        Assert.Equal(Start, entries[0].OccurredAt);
        Assert.Equal("adapter0", entries[0].DeviceId);
        Assert.Equal("s-1", entries[0].SessionId.Value);
        Assert.Equal("No space left on device", entries[0].Detail);
        Assert.Equal(DiagnosticReason.DeviceFaulted, entries[1].Reason);
        Assert.Equal(Start.AddMinutes(1), entries[1].OccurredAt);
        Assert.True(entries[1].SessionId.IsUnset);
    }

    [Fact]
    public void TheStoreKeepsTheNewestAndForgetsTheOldest()
    {
        var store = new DiagnosticsStore(new ManualTimeProvider(Start), capacity: 4);

        for (var index = 0; index < 10; index++)
        {
            store.Report(DiagnosticReason.MeasurementFaulted, $"fault-{index}");
        }

        var entries = store.Snapshot();

        Assert.Equal(4, entries.Count);
        Assert.Equal("fault-6", entries[0].Detail);
        Assert.Equal("fault-9", entries[^1].Detail);
    }

    [Fact]
    public async Task EveryReportSignalsTheDiagnosticsEvent()
    {
        var hub = new DriverEventHub();
        var store = new DiagnosticsStore(new ManualTimeProvider(Start), hub);

        Assert.True(hub.TryListen(out var listener));

        using (listener)
        {
            store.Report(DiagnosticReason.TuningLost, "the carrier vanished");

            var heard = await listener.Take(
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token
            );

            Assert.Contains(DriverEvents.Diagnostics, heard);
        }
    }
}
