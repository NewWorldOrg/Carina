using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class NotificationIntakeDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan LooksBack = TimeSpan.FromMinutes(30);

    private readonly HeldSources sources = new();

    private readonly HeldNotices notices = new();

    private NotificationIntakeDomainService Service => new(sources, notices, new FixedTimeProvider(new DateTimeOffset(Now)));

    [Fact(DisplayName = "BR-NC-004: the ledgers are read from thirty minutes back, and only what happened within that and up to now is taken, oldest first")]
    public async Task OnlyWhatHappenedWithinTheLookBackIsTaken()
    {
        Guid early = Guid.NewGuid();
        Guid edge = Guid.NewGuid();
        Guid late = Guid.NewGuid();
        Guid ahead = Guid.NewGuid();
        sources.Sources.Add(new Said(
            [
                Happened(late, Now),
                Happened(early, Now - LooksBack - TimeSpan.FromTicks(1)),
                Happened(edge, Now - LooksBack),
                Happened(ahead, Now.AddTicks(1)),
            ]));

        NoticeHappenings found = await Service.FreshAsync(LooksBack, Cancel);

        Assert.Equal([Now - LooksBack], sources.Asked);
        Assert.Equal(Now, found.At);
        Assert.Equal(2, found.Seen);
        Assert.Equal([edge, late], found.Fresh.Select(happening => happening.SourceId));
    }

    [Fact(DisplayName = "BR-ND-004: a happening of a kind and a row the notice ledger already holds is seen and not taken again")]
    public async Task AHappeningAlreadyHeldIsNotTakenAgain()
    {
        Guid row = Guid.NewGuid();
        sources.Sources.Add(new Said(
            [
                Happened(row, Now.AddMinutes(-2), NoticeKind.RecordingStarted),
                Happened(row, Now.AddMinutes(-1), NoticeKind.RecordingCompleted),
            ]));
        notices.Held.Add(new NoticeKey(NoticeKind.RecordingStarted, row));

        NoticeHappenings found = await Service.FreshAsync(LooksBack, Cancel);

        Assert.Equal(2, found.Seen);
        Assert.Equal([NoticeKind.RecordingCompleted], found.Fresh.Select(happening => happening.Kind));
    }

    [Fact(DisplayName = "BR-NC-004: a look that finds nothing within the look-back asks the notice ledger nothing")]
    public async Task ALookThatFindsNothingAsksNothing()
    {
        NoticeHappenings found = await Service.FreshAsync(LooksBack, Cancel);

        Assert.Equal(0, found.Seen);
        Assert.Empty(found.Fresh);
        Assert.Equal(0, notices.Asked);
    }

    private static NoticeHappening Happened(Guid row, DateTime at, NoticeKind kind = NoticeKind.RecordingFailed)
        => new(kind, row, at, new Said([]));

    private sealed record Said(IReadOnlyList<NoticeHappening> Told) : NoticeSource
    {
        public override IEnumerable<NoticeHappening> Happenings() => Told;
    }

    private sealed class HeldSources : INoticeSourceReader
    {
        public List<NoticeSource> Sources { get; } = [];

        public List<DateTime> Asked { get; } = [];

        public Task<IReadOnlyList<NoticeSource>> SinceAsync(DateTime since, CancellationToken cancellationToken)
        {
            Asked.Add(since);

            return Task.FromResult<IReadOnlyList<NoticeSource>>([.. Sources]);
        }
    }

    private sealed class HeldNotices : INoticeRepository
    {
        public HashSet<NoticeKey> Held { get; } = [];

        public int Asked { get; private set; }

        public Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken) => Task.FromResult<Notice?>(null);

        public Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
        {
            Asked++;

            return Task.FromResult<IReadOnlySet<NoticeKey>>(Held.Where(key => sourceIds.Contains(key.SourceId)).ToHashSet());
        }

        public Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<int> RemoveBeforeAsync(DateTime before, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
