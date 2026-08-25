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
    private static readonly TimeSpan BudgetOnAWarmCache = TimeSpan.FromSeconds(1);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int MeasuredRuns = 3;

    private const long MostBlocksForOnePage = 2_000;

    private const long MostBlocksForOneCount = 20_000;

    private const string TheOneShapeThatGuardsBothRegressions =
        "The year-back search without a keyword is the only shape that catches both the union reshape and the "
        + "index cover on every run. The keyword shape beside it flips its plan with the statistics sample, and "
        + "was measured passing against both regressions on some runs: it corroborates, it cannot stand in.";

    [Fact]
    public async Task ASearchThatStartsAYearBackMergesTheLayersInIndexOrderRatherThanSortingTheArchive()
    {
        Measured taken = await MeasuredAsync(
            ProgrammeSearch.For(null, ProgrammeSearchScale.Anchor.AddDays(-365), null)!,
            nameof(ASearchThatStartsAYearBackMergesTheLayersInIndexOrderRatherThanSortingTheArchive));

        Assert.True(taken.Page.NodeTypes.Contains("Merge Append"), TheOneShapeThatGuardsBothRegressions);
        Assert.True(!taken.Page.NodeTypes.Contains("Subquery Scan"), TheOneShapeThatGuardsBothRegressions);
        Assert.True(taken.Count.NodeTypes.Contains("Index Only Scan"), TheOneShapeThatGuardsBothRegressions);
        Assert.Contains("archived_programme", taken.Page.Relations);
        Assert.Contains("programme", taken.Page.Relations);

        Assert.Equal(418_660, taken.Found.Total);
        Assert.Equal(50, taken.Found.Items.Count);
        Assert.All(taken.Found.Items, match => Assert.True(match.IsArchived));
        Assert.Equal(ProgrammeSearchScale.ArchiveStartOfSlot(57), taken.Found.Items[0].StartsAt);
        Assert.Equal(1024, taken.Found.Items[0].ServiceId.Value);
        Assert.Equal(11_140, taken.Found.Items[0].EventId.Value);
        Assert.Equal(ProgrammeSearchScale.ArchiveStartOfSlot(59), taken.Found.Items[49].StartsAt);
        Assert.Equal(1033, taken.Found.Items[49].ServiceId.Value);
        Assert.Equal(11_189, taken.Found.Items[49].EventId.Value);
    }

    [Fact]
    public async Task ASearchThatOnlyNamesAnEndDateNeverReachesTheArchive()
    {
        Measured taken = await MeasuredAsync(
            ProgrammeSearch.For(null, null, ProgrammeSearchScale.Anchor.AddDays(3))!,
            nameof(ASearchThatOnlyNamesAnEndDateNeverReachesTheArchive));

        Assert.Contains("programme", taken.Page.Relations);
        Assert.Contains("programme", taken.Count.Relations);
        Assert.DoesNotContain("archived_programme", taken.Page.Relations);
        Assert.DoesNotContain("archived_programme", taken.Count.Relations);

        Assert.Equal(3_410, taken.Found.Total);
        Assert.All(taken.Found.Items, match => Assert.False(match.IsArchived));
    }

    [Fact]
    public async Task AKeywordSearchThatStartsAYearBackTakesTheSameOrderedPath()
    {
        Measured taken = await MeasuredAsync(
            ProgrammeSearch.For("第7回", ProgrammeSearchScale.Anchor.AddDays(-365), null)!,
            nameof(AKeywordSearchThatStartsAYearBackTakesTheSameOrderedPath));

        Assert.Contains("Merge Append", taken.Page.NodeTypes);
        Assert.DoesNotContain("Subquery Scan", taken.Page.NodeTypes);

        Assert.Equal(4_317, taken.Found.Total);
        Assert.Equal(50, taken.Found.Items.Count);
    }

    private async Task<Measured> MeasuredAsync(ProgrammeSearch looking, string shape)
    {
        var recorded = new RecordedCommands();
        await using CarinaDbContext context = scale.Open(recorded);
        var searches = new ProgrammeSearchRepository(context);

        PaginatedList<ProgrammeMatch> found = await searches.SearchAsync(
            looking,
            ProgrammeSearchScale.Anchor,
            Cancel);

        Assert.Equal(2, recorded.Seen.Count);

        QueryPlan? counting = null;
        QueryPlan? paging = null;

        foreach (RecordedCommand carried in recorded.Seen)
        {
            QueryPlan plan = await RecordedCommands.PlanForAsync(scale.ConnectionString, carried, Cancel);
            bool page = carried.Text.Contains("LIMIT", StringComparison.Ordinal);
            string which = page ? "page" : "count";

            output.WriteLine($"--- {shape}: {which} sql ---");
            output.WriteLine(carried.Text);
            output.WriteLine($"--- {shape}: {which} plan, {plan.SharedBlocks} shared blocks ---");
            output.WriteLine(string.Join(" / ", plan.NodeTypes));
            output.WriteLine(plan.Json);

            if (page)
            {
                paging = plan;
            }
            else
            {
                counting = plan;
            }
        }

        Assert.NotNull(counting);
        Assert.NotNull(paging);

        Assert.True(
            paging.SharedBlocks is > 0 && paging.SharedBlocks <= MostBlocksForOnePage,
            $"{shape} read {paging.SharedBlocks} shared blocks to hand back one page, wanted between 1 and "
            + $"{MostBlocksForOnePage}. Sorting the archive instead of merging it in index order costs about "
            + "sixty thousand; zero means the plan was never measured at all.");

        Assert.True(
            counting.SharedBlocks is > 0 && counting.SharedBlocks <= MostBlocksForOneCount,
            $"{shape} read {counting.SharedBlocks} shared blocks to count, wanted between 1 and "
            + $"{MostBlocksForOneCount}. Counting from the heap instead of the covering index costs about "
            + "sixty thousand; zero means the plan was never measured at all.");

        var runs = new List<TimeSpan>();

        for (int run = 0; run < MeasuredRuns; run++)
        {
            long started = Stopwatch.GetTimestamp();
            found = await searches.SearchAsync(looking, ProgrammeSearchScale.Anchor, Cancel);
            runs.Add(Stopwatch.GetElapsedTime(started));
        }

        output.WriteLine($"--- {shape}: {ProgrammeSearchScale.ArchivedRows} archived + "
            + $"{ProgrammeSearchScale.HotRows} hot, total {found.Total}. The wall clock below moves three to "
            + "five times with the page cache; the block counts above do not, which is why they are the gate ---");

        foreach (TimeSpan took in runs)
        {
            output.WriteLine($"{took.TotalMilliseconds:F1} ms");
        }

        Assert.All(runs, took => Assert.True(
            took < BudgetOnAWarmCache,
            $"{shape} answered in {took.TotalMilliseconds:F1} ms, over the "
            + $"{BudgetOnAWarmCache.TotalMilliseconds:F0} ms budget on a warm cache."));

        return new Measured(found, counting, paging);
    }

    private sealed record Measured(PaginatedList<ProgrammeMatch> Found, QueryPlan Count, QueryPlan Page);
}
