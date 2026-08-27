using Carina.Domain.Integrity;

namespace Carina.Infrastructure.Integrity;

public sealed record IntegrityRun
{
    private IntegrityRun(IntegrityReport? swept, IntegrityCheckId? running)
    {
        Swept = swept;
        Running = running;
    }

    public IntegrityReport? Swept { get; }

    public IntegrityCheckId? Running { get; }

    public bool AlreadyRunning => Swept is null;

    public static IntegrityRun Of(IntegrityReport swept)
    {
        ArgumentNullException.ThrowIfNull(swept);

        return new IntegrityRun(swept, null);
    }

    public static IntegrityRun RefusedBecauseOneIsRunning(IntegrityCheckId running)
    {
        ArgumentNullException.ThrowIfNull(running);

        return new IntegrityRun(null, running);
    }
}
