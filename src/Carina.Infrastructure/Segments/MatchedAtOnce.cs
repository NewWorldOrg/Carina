using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// The recordings followed to their end and matched against the catalog at once, told by the matching and
/// taken by detection, which detects them at once rather than waiting for spare time. What is told lives
/// only as long as the process; a recording told and not yet taken when it stops is detected later as any
/// other.
/// </summary>
public sealed class MatchedAtOnce
{
    private readonly Channel<RecordingId> told = Channel.CreateUnbounded<RecordingId>(new UnboundedChannelOptions { SingleReader = true });

    public void Tell(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        told.Writer.TryWrite(id);
    }

    public bool TryTake([NotNullWhen(true)] out RecordingId? id) => told.Reader.TryRead(out id);

    /// <summary>
    /// Completes once a recording is told and not yet taken.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken) => await told.Reader.WaitToReadAsync(cancellationToken);
}
