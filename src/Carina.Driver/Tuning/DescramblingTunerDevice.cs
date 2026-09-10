using Carina.Driver.Descrambling;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tuning;

public sealed class DescramblingTunerDevice : ITunerDevice
{
    public static readonly TimeSpan BetweenAsksForTheCard = TimeSpan.FromSeconds(5);

    private readonly ITunerDevice source;

    private readonly IDescramblerFactory? cards;

    private readonly TimeProvider time;

    private readonly ILogger? logger;

    private readonly Lock gate = new();

    private IDescrambler? descrambler;

    private DateTimeOffset asksAgainAt;

    private bool closed;

    public DescramblingTunerDevice(
        ITunerDevice source,
        IDescrambler? descrambler,
        IDescramblerFactory? cards = null,
        TimeProvider? time = null,
        ILogger? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);

        if (descrambler is null && cards is null)
        {
            throw new ArgumentException(
                "A tuner is only read through a card when there is one to hand or somewhere to ask for one.",
                nameof(cards)
            );
        }

        this.source = source;
        this.descrambler = descrambler;
        this.cards = cards;
        this.time = time ?? TimeProvider.System;
        this.logger = logger;
        asksAgainAt = this.time.GetUtcNow() + BetweenAsksForTheCard;
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

            IDescrambler? card = TheCard();
            if (card is null)
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

    private IDescrambler? TheCard()
    {
        lock (gate)
        {
            if (descrambler is not null || closed || cards is null)
            {
                return descrambler;
            }

            DateTimeOffset now = time.GetUtcNow();
            if (now < asksAgainAt)
            {
                return null;
            }

            asksAgainAt = now + BetweenAsksForTheCard;

            IDescrambler? answered = cards.Open();
            if (answered is null)
            {
                return null;
            }

            descrambler = answered;

            logger?.LogInformation(
                "A card answered the reader again, so what this tuner carries is unscrambled from here on."
            );

            return answered;
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

        LetGoOf(card);

        logger?.LogError(
            stopped,
            "The card stopped answering, so this tuner is read as it comes until a card answers again and what it carries stays scrambled until then; the scrambled-packet count says how much."
        );

        return swallowed.Length is 0 ? asRead : [.. swallowed, .. asRead];
    }

    private void LetGoOf(IDescrambler card)
    {
        lock (gate)
        {
            if (!ReferenceEquals(descrambler, card))
            {
                return;
            }

            descrambler = null;
            asksAgainAt = time.GetUtcNow() + BetweenAsksForTheCard;
        }

        card.Dispose();
    }

    public void Dispose()
    {
        IDescrambler? card;

        lock (gate)
        {
            closed = true;
            card = descrambler;
            descrambler = null;
        }

        try
        {
            card?.Dispose();
        }
        finally
        {
            source.Dispose();
        }
    }
}
