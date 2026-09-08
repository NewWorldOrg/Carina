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

        foreach (MigrationOmission omission in report.Omissions.OrderBy(omission => omission.Subject))
        {
            said.AppendLine(
                CultureInfo.InvariantCulture,
                $"Nothing was done about {omission.Subject}: {omission.Ground}.");
        }

        return said.ToString();
    }

    private static string Pass(MigrationPass pass)
        => pass is MigrationPass.Rehearsal ? "A rehearsal" : "A run for real";
}
