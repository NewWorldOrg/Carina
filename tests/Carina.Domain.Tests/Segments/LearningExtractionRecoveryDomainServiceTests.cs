using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningExtractionRecoveryDomainServiceTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Standings worklist = new();

    private LearningExtractionRecoveryDomainService Service => new(worklist);

    [Theory(DisplayName = "BR-CS-001: after a start, a record left following is put back to wait unless its recording is still being written, and then it is followed again from the head")]
    [InlineData(RecordingStanding.InFlight, false)]
    [InlineData(RecordingStanding.Ended, true)]
    [InlineData(null, true)]
    public async Task ARecordLeftFollowingIsPutBackUnlessItsRecordingGoesOn(RecordingStanding? standing, bool putBack)
    {
        LearningExtraction left = LearningExtraction.Following(RecordingId.New(), LearningExtractionTests.Programme(), ExtractionVersion.Current, Noon);

        worklist.Held[left.RecordingId] = standing;

        Assert.Equal(putBack, await Service.PutsBackAsync(left, CancellationToken.None));
    }

    private sealed class Standings : ILearningWorklist
    {
        public Dictionary<RecordingId, RecordingStanding?> Held { get; } = [];

        public Task<IReadOnlyList<Recording>> BeingRecordedAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Recording>>([]);

        public Task<RecordingStanding?> StandingAsync(RecordingId id, CancellationToken cancellationToken)
            => Task.FromResult(Held.GetValueOrDefault(id));
    }
}
