using System.Text;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.Fixtures.DataBroadcast;
using Carina.Infrastructure.Tests.Notifications;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Unit.Infra.Notifications;

[Trait("Category", "Unit")]
public sealed class NoticeDispatchTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("""{"said":"作り物"}""");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-NC-002: a destination is sent one notice at a time, while another destination is not kept waiting")]
    public async Task ADestinationIsSentOneNoticeAtATime()
    {
        TaskCompletionSource<CannedAnswer> held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using DeliveryBench bench = new(request => request.Target.EndsWith("wxyz", StringComparison.Ordinal)
            ? held.Task
            : Task.FromResult(CannedAnswer.Of(204, [])));
        NotificationDestinationId slow = NotificationDestinationId.New();
        NotificationDestinationId other = NotificationDestinationId.New();

        Task<NoticeAnswer> first = bench.Dispatch.SendAsync(slow, WebhookRig.Slack, Payload, Cancel);
        await Eventually.Happens(() => bench.Rig.Server.Heard.Count is 1, "the first notice reaches the webhook");
        Task<NoticeAnswer> second = bench.Dispatch.SendAsync(slow, WebhookRig.Slack, Payload, Cancel);

        NoticeAnswer elsewhere = await bench.Dispatch.SendAsync(other, WebhookRig.Discord, Payload, Cancel);

        Assert.True(elsewhere.IsTaken);
        Assert.Equal(2, bench.Rig.Server.Heard.Count);
        Assert.False(second.IsCompleted);

        held.SetResult(CannedAnswer.Text(200, "ok"));

        Assert.True((await first).IsTaken);
        Assert.True((await second).IsTaken);
        Assert.Equal(3, bench.Rig.Server.Heard.Count);
    }

    [Fact(DisplayName = "BR-NC-002: a destination has room for thirty notices in sixty seconds, and for the next when the oldest is sixty seconds old")]
    public async Task ADestinationHasRoomForThirtyInSixtySeconds()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestinationId destination = NotificationDestinationId.New();
        NotificationDestinationId other = NotificationDestinationId.New();

        for (int index = 0; index < 30; index++)
        {
            Assert.True(bench.Dispatch.HasRoomFor(destination));
            await bench.Dispatch.SendAsync(destination, WebhookRig.Slack, Payload, Cancel);
            bench.Clock.Turn(TimeSpan.FromSeconds(1));
        }

        Assert.False(bench.Dispatch.HasRoomFor(destination));
        Assert.True(bench.Dispatch.HasRoomFor(other));

        bench.Clock.Turn(TimeSpan.FromSeconds(29));
        Assert.False(bench.Dispatch.HasRoomFor(destination));

        bench.Clock.Turn(TimeSpan.FromSeconds(1));
        Assert.True(bench.Dispatch.HasRoomFor(destination));
    }

    [Fact(DisplayName = "BR-NC-001: after a 429 a destination has no room until the wait the other end asked for is over")]
    public async Task AfterA429ADestinationHasNoRoomUntilTheWaitIsOver()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(429, [], "Retry-After: 20"));
        NotificationDestinationId destination = NotificationDestinationId.New();

        NoticeAnswer answer = await bench.Dispatch.SendAsync(destination, WebhookRig.Slack, Payload, Cancel);

        Assert.Equal(NoticeFailure.RateLimited, answer.Failure);
        Assert.False(bench.Dispatch.HasRoomFor(destination));

        bench.Clock.Turn(TimeSpan.FromSeconds(19));
        Assert.False(bench.Dispatch.HasRoomFor(destination));

        bench.Clock.Turn(TimeSpan.FromSeconds(1));
        Assert.True(bench.Dispatch.HasRoomFor(destination));
    }

    [Fact(DisplayName = "BR-NC-002: a destination that is forgotten starts with all its room again")]
    public async Task ADestinationThatIsForgottenStartsAgain()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(429, [], "Retry-After: 600"));
        NotificationDestinationId destination = NotificationDestinationId.New();
        await bench.Dispatch.SendAsync(destination, WebhookRig.Slack, Payload, Cancel);

        bench.Dispatch.Forget(destination);

        Assert.True(bench.Dispatch.HasRoomFor(destination));
    }
}
