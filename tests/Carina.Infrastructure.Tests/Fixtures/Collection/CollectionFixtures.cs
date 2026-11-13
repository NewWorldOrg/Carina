using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Tests.Fixtures.Clocks;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.Fixtures.Collection;

internal static class CollectionFixtures
{
    /// <summary>
    /// Has the container write the guide through a <see cref="ProgrammeWriter"/> on the programmes it holds, on a clock
    /// that stands still, with no guard around the writes and nobody told of them.
    /// </summary>
    public static void WriterOnAStillClock(IServiceCollection replace)
        => replace.AddScoped(scope => new ProgrammeWriter(
            scope.GetRequiredService<IProgrammeRepository>(),
            new UnguardedWrites(),
            new StillClock(),
            new SilentEvents(),
            new CountedNotices()));
}
