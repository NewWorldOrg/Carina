using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Carina.Infrastructure.Tests.DbIntegration.Domain.Segments.ActionsLaidWithinAnHour;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SeekEventRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private readonly ServiceProvider container = database.Infrastructure();

    private readonly List<Guid> laid = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (CarinaDbContext context = database.Open())
        {
            Guid[] ours = [.. laid];
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_seek_event WHERE recording_id = ANY({ours})");
        }

        await container.DisposeAsync();
    }

    [Theory(DisplayName = "BR-CV-004: the store keeps a seek exactly when the seek says it is taken beside those its recording kept")]
    [MemberData(nameof(Arms), MemberType = typeof(ActionsLaidWithinAnHour))]
    public async Task TheStoreKeepsASeekWhenTheSeekSaysSo(string arm, int withinTheHalfHour, double[] secondsFromTheNew)
    {
        RecordingId recording = RecordingId.New();
        laid.Add(recording.Value);
        DateTime[] kept = Laid(withinTheHalfHour, secondsFromTheNew);
        await LayAsync(container, "INSERT INTO segment_seek_event (recording_id, happened_at, event, means, from_at, to_at) SELECT @recording, at, 'Forward', 'Bar', interval '915 seconds', interval '1002 seconds' FROM unnest(@laid) AS at", recording, kept);
        SeekEvent seek = SeekEvent.Of(recording, new ViewerSeek(Noon, SeekAction.Forward, SeekMeans.Bar, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002)));

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.True(seek.IsTakenBeside(kept) == await scope.ServiceProvider.GetRequiredService<ISeekEventRepository>().TakeAsync(seek, CancellationToken.None), arm);
    }
}
