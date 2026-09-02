using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class TranscodeBudget : ITranscodeBudget
{
    private readonly TranscodeBudgetSettings settings;

    private readonly Lock counting = new();

    private int running;

    public TranscodeBudget(TranscodeBudgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.settings = settings;
    }

    public int Running
    {
        get
        {
            lock (counting)
            {
                return running;
            }
        }
    }

    public TranscodeClaim Claim(TranscodePurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "A transcoder is raised for one of the two purposes there are.");
        }

        lock (counting)
        {
            if (running >= settings.AtOnce)
            {
                return TranscodeClaim.Refused(new TranscodeCeiling(running, settings.AtOnce));
            }

            running++;

            return TranscodeClaim.Seated(new Seat(this, purpose, running, settings.AtOnce));
        }
    }

    private sealed class Seat(TranscodeBudget budget, TranscodePurpose purpose, int place, int atOnce) : ITranscodeSeat
    {
        private bool letGo;

        public TranscodePurpose Purpose { get; } = purpose;

        public int Place { get; } = place;

        public int AtOnce { get; } = atOnce;

        public void Dispose()
        {
            lock (budget.counting)
            {
                if (letGo)
                {
                    return;
                }

                letGo = true;
                budget.running--;
            }
        }
    }
}
