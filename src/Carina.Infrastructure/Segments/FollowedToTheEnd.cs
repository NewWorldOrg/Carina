using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Segments;

public sealed class FollowedToTheEnd
{
    private readonly Channel<RecordingId> told = Channel.CreateUnbounded<RecordingId>(new UnboundedChannelOptions { SingleReader = true });

    public void Tell(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        told.Writer.TryWrite(id);
    }

    public bool TryTake([NotNullWhen(true)] out RecordingId? id) => told.Reader.TryRead(out id);

    public async Task WaitAsync(CancellationToken cancellationToken) => await told.Reader.WaitToReadAsync(cancellationToken);
}
