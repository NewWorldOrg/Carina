using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Api.Tests.Feature.Thumbnails;

internal sealed class HeldFrames : IScrubFrames
{
    private readonly ScrubFrame answer;

    public HeldFrames(ScrubFrame answer) => this.answer = answer;

    public List<TimeSpan> AskedAt { get; } = [];

    public List<RecordingId> AskedAbout { get; } = [];

    public Task<ScrubFrame> AtAsync(RecordingId id, TimeSpan at, CancellationToken cancellationToken)
    {
        AskedAbout.Add(id);
        AskedAt.Add(at);

        return Task.FromResult(answer);
    }
}
