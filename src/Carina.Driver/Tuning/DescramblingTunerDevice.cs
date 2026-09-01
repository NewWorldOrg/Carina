using Carina.Driver.Descrambling;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tuning;

public sealed class DescramblingTunerDevice : ITunerDevice
{
    private readonly ITunerDevice source;

    private readonly ILogger? logger;

    private IDescrambler? descrambler;

    public DescramblingTunerDevice(
        ITunerDevice source,
        IDescrambler descrambler,
        ILogger? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(descrambler);

        this.source = source;
        this.descrambler = descrambler;
        this.logger = logger;
    }

    public long Overflows => source.Overflows;

    public ISignalQualitySource? Quality => source.Quality;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            byte[] asRead = source.Read(count, cancellationToken);

            IDescrambler? card = descrambler;
            if (card is null || asRead.Length is 0)
            {
                return asRead;
            }

            byte[] readable;

            try
            {
                readable = card.Descramble(asRead);
            }
            catch (DescramblingException stopped)
            {
                return CarryOnWithoutTheCard(card, stopped, asRead);
            }

            if (readable.Length > 0)
            {
                return readable;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private byte[] CarryOnWithoutTheCard(
        IDescrambler card,
        DescramblingException stopped,
        byte[] asRead
    )
    {
        byte[] swallowed;

        try
        {
            swallowed = card.WhatItCouldNotRead();
        }
        catch (DescramblingException)
        {
            swallowed = [];
        }

        if (ReferenceEquals(Interlocked.CompareExchange(ref descrambler, null, card), card))
        {
            card.Dispose();
        }

        logger?.LogError(
            stopped,
            "The card stopped answering, so this tuner is read as it comes from here on and what it carries stays scrambled; the scrambled-packet count says how much."
        );

        return swallowed.Length is 0 ? asRead : [.. swallowed, .. asRead];
    }

    public void Dispose()
    {
        try
        {
            Interlocked.Exchange(ref descrambler, null)?.Dispose();
        }
        finally
        {
            source.Dispose();
        }
    }
}
