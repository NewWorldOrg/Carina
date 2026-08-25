using System.Diagnostics;

using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Xunit.Abstractions;

namespace Carina.Infrastructure.Tests;

[Trait("Category", "Scale")]
public sealed class ProgrammeSearchScaleTests(ProgrammeSearchScale scale, ITestOutputHelper output)
    : IClassFixture<ProgrammeSearchScale>
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int MeasuredRuns = 3;

    [Fact]
    public async Task ASearchThatStartsAYearBackAnswersInsideASecond()
    {
        ProgrammeSearch looking = ProgrammeSearch.For(
            null,
            ProgrammeSearchScale.Anchor.AddDays(-365),
            null)!;

        PaginatedList<ProgrammeMatch> found = await MeasuredAsync(looking, nameof(
            ASearchThatStartsAYearBackAnswersInsideASecond));

        Assert.Equal(418_660, found.Total);
        Assert.Equal(50, found.Items.Count);
        Assert.All(found.Items, match => Assert.True(match.IsArchived));
        Assert.Equal(ProgrammeSearchScale.ArchiveStartOfSlot(57), found.Items[0].StartsAt);
        Assert.Equal(1024, found.Items[0].ServiceId.Value);
        Assert.Equal(11_140, found.Items[0].EventId.Value);
        Assert.Equal(ProgrammeSearchScale.ArchiveStartOfSlot(59), found.Items[49].StartsAt);
        Assert.Equal(1033, found.Items[49].ServiceId.Value);
        Assert.Equal(11_189, found.Items[49].EventId.Value);
    }

    [Fact]
    public async Task ASearchThatOnlyNamesAnEndDateNeverReachesTheArchive()
    {
        ProgrammeSearch looking = ProgrammeSearch.For(
            null,
            null,
            ProgrammeSearchScale.Anchor.AddDays(3))!;

        PaginatedList<ProgrammeMatch> found = await MeasuredAsync(looking, nameof(
            ASearchThatOnlyNamesAnEndDateNeverReachesTheArchive));

        Assert.Equal(3_410, found.Total);
        Assert.All(found.Items, match => Assert.False(match.IsArchived));
    }

    [Fact]
    public async Task AKeywordSearchThatStartsAYearBackAnswersInsideASecond()
    {
        ProgrammeSearch looking = ProgrammeSearch.For(
            "第7回",
            ProgrammeSearchScale.Anchor.AddDays(-365),
            null)!;

        PaginatedList<ProgrammeMatch> found = await MeasuredAsync(looking, nameof(
            AKeywordSearchThatStartsAYearBackAnswersInsideASecond));

        Assert.Equal(4_317, found.Total);
        Assert.Equal(50, found.Items.Count);
    }

    private async Task<PaginatedList<ProgrammeMatch>> MeasuredAsync(ProgrammeSearch looking, string shape)
    {
        var recorded = new RecordedCommands();
        await using CarinaDbContext context = scale.Open(recorded);
        var searches = new ProgrammeSearchRepository(context);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            looking,
            ProgrammeSearchScale.Anchor,
            Cancel);

        foreach (RecordedCommand carried in recorded.Seen)
        {
            output.WriteLine($"--- {shape}: sql ---");
            output.WriteLine(carried.Text);
            output.WriteLine($"--- {shape}: plan ---");
            output.WriteLine(await RecordedCommands.PlanForAsync(scale.ConnectionString, carried, Cancel));
        }

        var runs = new List<TimeSpan>();

        for (int run = 0; run < MeasuredRuns; run++)
        {
            long started = Stopwatch.GetTimestamp();
            found = await searches.SearchAsync(looking, ProgrammeSearchScale.Anchor, Cancel);
            runs.Add(Stopwatch.GetElapsedTime(started));
        }

        output.WriteLine($"--- {shape}: {ProgrammeSearchScale.ArchivedRows} archived + "
            + $"{ProgrammeSearchScale.HotRows} hot, total {found.Total} ---");

        foreach (TimeSpan took in runs)
        {
            output.WriteLine($"{took.TotalMilliseconds:F1} ms");
        }

        Assert.All(runs, took => Assert.True(
            took < Budget,
            $"{shape} answered in {took.TotalMilliseconds:F1} ms, over the {Budget.TotalMilliseconds:F0} ms budget."));

        return found;
    }
}
