using Carina.Domain.Quality;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualityIncidentRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Started = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    private static readonly Threshold Applied = Threshold.Provisionally(300, 0, Started);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "BR-QS-002: the incidents the store reads as unsettled are the ones each says has not settled, so every incident awaiting its notice is among them")]
    public async Task TheIncidentsReadAsUnsettledAreTheOnesThatHaveNotSettled()
    {
        await ClearAsync();

        List<QualityIncident> laid = [];

        foreach (TimeSpan apart in new[] { TimeSpan.Zero, Microsecond, TimeSpan.FromMinutes(5) })
        {
            laid.Add(Incident());

            QualityIncident notified = Incident();
            notified.Notify(Started.Add(apart));
            laid.Add(notified);

            QualityIncident resolved = Incident();
            resolved.Resolve(Started.Add(apart));
            laid.Add(resolved);

            QualityIncident notifiedThenResolved = Incident();
            notifiedThenResolved.Notify(Started.Add(apart));
            notifiedThenResolved.Resolve(Started.Add(apart + apart));
            laid.Add(notifiedThenResolved);
        }

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            CarinaDbContext context = writing.ServiceProvider.GetRequiredService<CarinaDbContext>();
            context.AddRange(laid);
            await context.SaveChangesAsync(Cancel);
        }

        IReadOnlyList<QualityIncident> stored = await ReadAllAsync();

        Assert.Contains(stored, incident => incident.AwaitsNotice);
        Assert.Contains(stored, incident => !incident.AwaitsNotice && !incident.HasSettled);
        Assert.Contains(stored, incident => incident.HasSettled && incident.ResolvedAt == incident.DetectedAt);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<QualityIncident> unsettled =
            await reading.ServiceProvider.GetRequiredService<IQualityIncidentRepository>().ListUnsettledAsync(Cancel);

        Assert.Equal(Ids(stored.Where(incident => !incident.HasSettled)), Ids(unsettled));
        Assert.Subset(Ids(unsettled).ToHashSet(), Ids(stored.Where(incident => incident.AwaitsNotice)).ToHashSet());
    }

    private static QualityIncident Incident()
        => QualityIncident.Detect(
            QualityIncidentId.New(),
            Started,
            QualityThresholdKey.SupplySilence,
            QualitySubject.Of(QualitySubjectKind.Tuner, "adapter0"),
            420,
            Applied,
            silence: SupplySilence.SignalSamples);

    private static Guid[] Ids(IEnumerable<QualityIncident> incidents)
        => [.. incidents.Select(incident => incident.Id.Value).Order()];

    private async Task<IReadOnlyList<QualityIncident>> ReadAllAsync()
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await reading.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualityIncident>()
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await clearing.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualityIncident>()
            .ExecuteDeleteAsync(Cancel);
    }
}
