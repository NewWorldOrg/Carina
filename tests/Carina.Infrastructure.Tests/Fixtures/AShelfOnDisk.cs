using System.Globalization;

using Carina.Domain.Migration;
using Carina.Domain.Programmes;

using static Carina.TestSupport.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Fixtures.Migration;

internal sealed class AShelfOnDisk(
    string directory,
    int count,
    IReadOnlyList<ServiceKey> stations,
    DateTime firstAired) : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public static void LayDown(string directory, int count, string notARecording)
    {
        foreach (int at in Enumerable.Range(1, count))
        {
            File.WriteAllBytes(Path.Combine(directory, FileOf(at)), new byte[188 * at]);
        }

        File.WriteAllBytes(Path.Combine(directory, notARecording), new byte[64]);
    }

    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Ledger(
            [
                .. Enumerable.Range(1, count).Select(at => new SourceRecording(
                    at,
                    string.Create(CultureInfo.InvariantCulture, $"a carried programme {at}"),
                    firstAired.AddHours(60 * (at - 1)),
                    firstAired.AddHours(60 * (at - 1)).AddMinutes(30),
                    stations[(at - 1) % stations.Count],
                    new EventId(5_000 + at))),
            ],
            [.. Enumerable.Range(1, count).Select(at => AsBroadcast(at, FileOf(at), 188 * at))]));

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SourceFile>>(
            [.. Directory
                .GetFiles(directory)
                .Order(StringComparer.Ordinal)
                .Select(file => OnDisk(Path.GetFileName(file), new FileInfo(file).Length))]);

    private static string FileOf(int at) => string.Create(CultureInfo.InvariantCulture, $"carried-{at:D2}.m2ts");
}
