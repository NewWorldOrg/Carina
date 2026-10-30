using Carina.Driver.CardLending;
using Carina.Driver.Configuration;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Descrambling;

public sealed class Descramblers : IDescramblerFactory
{
    private readonly Func<ISmartCardService> openService;

    private readonly ILogger? logger;

    private CardIdentity? cardInHand;

    private Descramblers(Func<ISmartCardService> openService, ILogger? logger)
    {
        this.openService = openService;
        this.logger = logger;
    }

    /// <summary>
    /// Cards asked for through the smart-card service <paramref name="openService"/> opens, a new one each time.
    /// </summary>
    public static Descramblers Over(Func<ISmartCardService> openService, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(openService);

        return new Descramblers(openService, logger);
    }

    /// <summary>
    /// What a tuner of this driver is read through: a card borrowed from <paramref name="loan"/> where there is one,
    /// otherwise the card in this machine's reader for a DVB tuner, and nothing for a synthetic one.
    /// </summary>
    public static IDescramblerFactory For(DriverConfiguration configuration, CardLoan? loan, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        TunerBackend backend = configuration.Tuner?.Backend ?? TunerBackend.Unspecified;
        if (backend is not (TunerBackend.Dvb or TunerBackend.Usb))
        {
            return NoDescrambling.Instance;
        }

        if (loan is not null)
        {
            return BorrowedCards.From(loan, logger);
        }

        return backend is TunerBackend.Dvb ? Probe(logger) : NoDescrambling.Instance;
    }

    /// <summary>
    /// Opens the card in this machine's reader each time it is called, for lending; where the smart-card service cannot
    /// be loaded, every call says so.
    /// </summary>
    public static Func<IConditionalAccess> CardsToLend(ILogger? logger)
    {
        PcscLibrary? library = PcscLibrary.Load(out string whyNot);
        if (library is null)
        {
            logger?.LogWarning("This driver has no card to lend: {Why}", whyNot);

            return () => throw new DescramblingException(whyNot);
        }

        return () => OpenCard(library.Open, settled: null);
    }

    public static IDescramblerFactory Probe(ILogger? logger)
    {
        PcscLibrary? library = PcscLibrary.Load(out string whyNot);
        if (library is null)
        {
            logger?.LogWarning(
                "This driver does not unscramble and does not offer to: {Why} What it records stays as the tuner gave it, and its scrambled-packet count says how much of that was scrambled.",
                whyNot
            );

            return NoDescrambling.Instance;
        }

        Descramblers descramblers = new(library.Open, logger);

        try
        {
            descramblers.TheCard().Dispose();

            logger?.LogInformation(
                "A card answered the reader, so this driver unscrambles what it records and says so in its greeting."
            );
        }
        catch (DescramblingException error)
        {
            logger?.LogWarning(
                "No card answered the reader when this driver started, so it asks again as each tuner opens and while one is being read: {Why}",
                error.Message
            );
        }

        return descramblers;
    }

    public bool Unscrambles => true;

    public CardIdentity? CardInHand => Volatile.Read(ref cardInHand);

    public IDescrambler? Open(HeadHold hold)
    {
        try
        {
            return new TransportStreamDescrambler(TheCard(), logger, hold: hold);
        }
        catch (DescramblingException error)
        {
            logger?.LogDebug(
                "The card was asked for and did not answer, so it is asked again: {Why}",
                error.Message
            );

            return null;
        }
    }

    private ConditionalAccessCard TheCard() =>
        OpenCard(openService, identity => Volatile.Write(ref cardInHand, identity));

    private static ConditionalAccessCard OpenCard(Func<ISmartCardService> openService, Action<CardIdentity>? settled)
    {
        ISmartCardService service;

        try
        {
            service = openService();
        }
        catch (SmartCardException refused)
        {
            throw new DescramblingException(refused.Message);
        }

        return ConditionalAccessCard.Open(service, settled);
    }
}
