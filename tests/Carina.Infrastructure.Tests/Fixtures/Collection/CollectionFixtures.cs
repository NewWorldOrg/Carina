using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Tests.Fixtures.Clocks;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Fixtures.Collection;

internal static class CollectionFixtures
{
    public static void WriterOnAStillClock(IServiceCollection replace)
        => replace.AddScoped(scope => new ProgrammeWriter(
            scope.GetRequiredService<IProgrammeRepository>(),
            new UnguardedWrites(),
            new StillClock(),
            new SilentEvents(),
            new CountedNotices()));
}
