using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LearningDataRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    private readonly List<Guid> recordings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using (CarinaDbContext context = database.Open())
        {
            Guid[] ours = [.. recordings];
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_learning_data WHERE recording_id = ANY({ours})");
        }

        await container.DisposeAsync();
    }

    [Fact(DisplayName = "whether captions are shown is kept and read back as a kind of learning data of its own")]
    public async Task WhetherCaptionsAreShownIsKept()
    {
        RecordingId recording = RecordingId.New();
        byte[] seconds = [.. Enumerable.Range(0, LearningData.ChunkSeconds).Select(second => second % 5 is 0 ? CaptionPresence.Shown : CaptionPresence.Hidden)];

        await KeepAsync(LearningDataBlock.Of(recording, LearningDataPart.OfCaptions(2, seconds), ExtractionVersion.Current, Noon));

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        LearningDataBlock? kept = await Data(scope).FindAsync(recording, LearningDataKind.CaptionPresence, 2, Cancel);

        Assert.NotNull(kept);
        Assert.True(kept.TryRead(out LearningDataPart? part));
        Assert.Equal(seconds, part.Captions.ToArray());
    }

    private async Task KeepAsync(LearningDataBlock block)
    {
        recordings.Add(block.RecordingId.Value);
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Data(scope).KeepAsync(block, Cancel);
    }

    private static ILearningDataRepository Data(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ILearningDataRepository>();
}
