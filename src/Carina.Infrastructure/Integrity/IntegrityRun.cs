using Carina.Domain.Integrity;

namespace Carina.Infrastructure.Integrity;

public sealed record IntegrityRun
{
    private IntegrityRun(IntegrityReport? swept)
    {
        Swept = swept;
    }

    public IntegrityReport? Swept { get; }

    public bool AlreadyRunning => Swept is null;

    public static IntegrityRun Of(IntegrityReport swept)
    {
        ArgumentNullException.ThrowIfNull(swept);

        return new IntegrityRun(swept);
    }

    public static IntegrityRun RefusedBecauseOneIsRunning() => new(swept: null);
}
