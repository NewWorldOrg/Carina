using Carina.Driver.Descrambling;

namespace Carina.Driver.Tuning;

public sealed class DescramblingTunerDevice : ITunerDevice
{
    public const long SwallowedWithoutAnAnswer = 8L * 1024 * 1024;

    private readonly ITunerDevice source;

    private readonly IDescrambler descrambler;

    private long swallowed;

    private bool hasAnswered;

    public DescramblingTunerDevice(ITunerDevice source, IDescrambler descrambler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(descrambler);

        this.source = source;
        this.descrambler = descrambler;
    }

    public long Overflows => source.Overflows;

    public ISignalQualitySource? Quality => source.Quality;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            byte[] asRead = source.Read(count, cancellationToken);
            if (asRead.Length is 0)
            {
                return asRead;
            }

            byte[] readable = descrambler.Descramble(asRead);
            if (readable.Length > 0)
            {
                hasAnswered = true;

                return readable;
            }

            swallowed += asRead.Length;

            if (!hasAnswered && swallowed > SwallowedWithoutAnAnswer)
            {
                throw new DescramblingException(
                    $"The descrambler took {swallowed} bytes from this tuner and handed none of it back, so this session would write an empty file rather than a scrambled one."
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public byte[] WhatIsHeldBack()
    {
        byte[] tail = source.WhatIsHeldBack();
        byte[] readable = tail.Length is 0 ? [] : descrambler.Descramble(tail);
        byte[] held = descrambler.WhatIsStillHeld();

        if (readable.Length is 0)
        {
            return held;
        }

        return held.Length is 0 ? readable : [.. readable, .. held];
    }

    public void Dispose()
    {
        try
        {
            descrambler.Dispose();
        }
        finally
        {
            source.Dispose();
        }
    }
}
