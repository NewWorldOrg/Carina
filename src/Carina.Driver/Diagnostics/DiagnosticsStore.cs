using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Driver.Events;

namespace Carina.Driver.Diagnostics;

public sealed class DiagnosticsStore(
    TimeProvider timeProvider,
    DriverEventHub? events = null,
    int capacity = DiagnosticsStore.DefaultCapacity
)
{
    public const int DefaultCapacity = 256;

    private readonly ConcurrentQueue<DiagnosticSnapshot> entries = new();

    public int Capacity => capacity;

    public IReadOnlyList<DiagnosticSnapshot> Snapshot() => [.. entries];

    public void Report(
        DiagnosticReason reason,
        string detail,
        string? deviceId = null,
        SessionId sessionId = default
    )
    {
        entries.Enqueue(
            new DiagnosticSnapshot(
                reason,
                timeProvider.GetUtcNow(),
                deviceId,
                sessionId,
                detail
            )
        );

        while (entries.Count > capacity && entries.TryDequeue(out _))
        { }

        events?.Signal(DriverEvents.Diagnostics);
    }
}
