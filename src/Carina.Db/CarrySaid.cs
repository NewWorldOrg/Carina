using System.Globalization;
using System.Text;

using Carina.Domain.Migration;

namespace Carina.Db;

public static class CarrySaid
{
    public static string Of(MigrationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder said = new();

        said.AppendLine(CultureInfo.InvariantCulture, $"{Pass(report.Run.Pass)} against {report.Run.Source.Value}.");

        foreach (MigrationTally tally in report.Tallies.OrderBy(tally => tally.Population))
        {
            said.AppendLine(
                CultureInfo.InvariantCulture,
                $"{tally.Population}: {tally.Offered} offered, {tally.Carried} carried, "
                + $"{tally.NotCarried} not carried, {tally.Unclassified} unclassified.");
        }

        foreach (IGrouping<(MigrationPopulation Population, MigrationRefusal Refusal), MigrationDetail> reason
            in report.Details
                .GroupBy(detail => (detail.Population, detail.Refusal))
                .OrderBy(reason => reason.Key.Population)
                .ThenBy(reason => reason.Key.Refusal))
        {
            said.AppendLine(
                CultureInfo.InvariantCulture,
                $"Not carried, {reason.Key.Population} {reason.Key.Refusal}: {reason.Count()}.");
        }

        foreach (IGrouping<MigrationChannelStanding, MigrationChannelProposal> standing
            in report.ChannelProposals.GroupBy(proposal => proposal.Standing).OrderBy(standing => standing.Key))
        {
            said.AppendLine(
                CultureInfo.InvariantCulture,
                $"Channel definitions, {standing.Key}: {standing.Count()}. Nothing was settled by this run.");
        }

        said.AppendLine(
            CultureInfo.InvariantCulture,
            $"Rules converted, every one of them turned off: {report.RuleProposals.Count}, of which "
            + $"{report.RuleProposals.Count(proposal => proposal.EnabledAtTheSource)} were on at the source.");

        foreach (MigrationStanding standing in report.Standings.OrderBy(standing => standing.Subject))
        {
            string stops = standing.WouldStopARunForReal ? " A run for real stops here." : string.Empty;

            said.AppendLine(
                CultureInfo.InvariantCulture,
                $"Before carrying, {standing.Subject}: {standing.Finding}.{stops}");
        }

        foreach (MigrationLoss loss in report.Losses.OrderBy(loss => loss.Subject))
        {
            said.AppendLine(
                CultureInfo.InvariantCulture,
                $"Carried and diminished, {loss.Subject}: {loss.Affected} rows.");
        }

        return said.ToString();
    }

    private static string Pass(MigrationPass pass)
        => pass is MigrationPass.Rehearsal ? "A rehearsal" : "A run for real";
}
