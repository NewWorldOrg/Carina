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

    private readonly HeadHold hold;

    private readonly Lock gate = new();

    private IDescrambler? descrambler;

    private DateTimeOffset asksAgainAt;

    private Task<IDescrambler?>? asking;

    private bool closed;

    public DescramblingTunerDevice(
        ITunerDevice source,
        IDescrambler? descrambler,
        IDescramblerFactory? cards = null,
        TimeProvider? time = null,
        ILogger? logger = null,
        HeadHold? hold = null
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
        this.hold = hold ?? HeadHold.EveryProgramme;
        asksAgainAt = this.time.GetUtcNow() + BetweenAsksForTheCard;
    }

    public long Overflows => source.Overflows;

    public ISignalQualitySource? Quality => source.Quality;

    /// <summary>
    /// The card being asked for while the tuner is read as it comes, or a finished task when nothing is being asked.
    /// </summary>
    public Task AskedForTheCard
    {
        get
        {
            lock (gate)
            {
                return asking ?? Task.CompletedTask;
            }
        }
    }

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

    /// <summary>
    /// The descrambler in hand, or null while there is none. Asking for one happens away from the read, so a card that
    /// is slow to answer, or a lender across the network, never holds the tuner's bytes back.
    /// </summary>
    private IDescrambler? TheCard()
    {
        lock (gate)
        {
            if (descrambler is not null || closed || cards is null)
            {
                return descrambler;
            }

            if (asking is not null)
            {
                if (!asking.IsCompleted)
                {
                    return null;
                }

                IDescrambler? answered = asking.Result;
                asking = null;

                if (answered is not null)
                {
                    descrambler = answered;
                    logger?.LogInformation(
                        "A card answered the reader again, so what this tuner carries is unscrambled from here on."
                    );

                    return answered;
                }
            }

            DateTimeOffset now = time.GetUtcNow();
            if (now < asksAgainAt)
            {
                return null;
            }

            asksAgainAt = now + BetweenAsksForTheCard;
            asking = Task.Run(Ask);

            return null;
        }
    }

    private IDescrambler? Ask()
    {
        try
        {
            return cards!.Open(hold);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            logger?.LogWarning(error, "Asking for the card failed, so it is asked for again later.");

            return null;
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
        Task<IDescrambler?>? unanswered;

        lock (gate)
        {
            closed = true;
            card = descrambler;
            descrambler = null;
            unanswered = asking;
            asking = null;
        }

        unanswered?.ContinueWith(
            static answered => answered.Result?.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

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
