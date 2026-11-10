using Carina.Domain.Notifications;

namespace Carina.Conventions.Tests.Fixtures;

public static class ProgrammeWatchFixtures
{
    public static int TakesOneAsAnArgument(ProgrammeWatch watch)
    {
        ArgumentNullException.ThrowIfNull(watch);

        return 1;
    }

    public static int TakesSeveralInAList(IReadOnlyList<ProgrammeWatch> watches)
    {
        ArgumentNullException.ThrowIfNull(watches);

        return watches.Count;
    }

    public static bool AsksOneWhetherItStands(object held, DateTime at)
        => ((ProgrammeWatch)held).StandsAt(at);

    public static Task ReadsTheLedger(object held, DateTime at)
        => ((IProgrammeWatchRepository)held).ListFollowedAsync(at, CancellationToken.None);

    public static async Task<int> ReadsTheLedgerAndWaitsAsync(object held, DateTime at)
    {
        await ((IProgrammeWatchRepository)held).ListFollowedAsync(at, CancellationToken.None).ConfigureAwait(false);

        return 1;
    }

    public static int MentionsNone(int given) => given + 1;
}
