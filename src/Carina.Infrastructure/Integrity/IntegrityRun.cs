using Carina.Domain.Integrity;

namespace Carina.Infrastructure.Integrity;

public sealed record IntegrityRun
{
    private IntegrityRun(IntegritySweep? swept)
    {
        Swept = swept;
    }

    public IntegritySweep? Swept { get; }

    public bool AlreadyRunning => Swept is null;

    public static IntegrityRun Of(IntegritySweep swept)
    {
        ArgumentNullException.ThrowIfNull(swept);

        return new IntegrityRun(swept);
    }

    public static IntegrityRun RefusedBecauseOneIsRunning() => new(swept: null);
}
