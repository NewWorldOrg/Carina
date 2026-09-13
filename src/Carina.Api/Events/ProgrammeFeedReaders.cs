using System.Diagnostics.CodeAnalysis;

using Carina.Domain.Programmes;

namespace Carina.Api.Events;

public sealed class ProgrammeFeedReaders
{
    private readonly SemaphoreSlim places;

    public ProgrammeFeedReaders(ProgrammeFeedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Limit = settings.ConcurrentReaders;
        ComeBackIn = settings.StatementTimeout;
        places = new SemaphoreSlim(Limit, Limit);
    }

    public int Limit { get; }

    public TimeSpan ComeBackIn { get; }

    public int Free => places.CurrentCount;

    public bool TryTake([NotNullWhen(true)] out IDisposable? place)
    {
        if (!places.Wait(0))
        {
            place = null;

            return false;
        }

        place = new Place(places);

        return true;
    }

    private sealed class Place(SemaphoreSlim places) : IDisposable
    {
        private int given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref given, 1) == 0)
            {
                places.Release();
            }
        }
    }
}
