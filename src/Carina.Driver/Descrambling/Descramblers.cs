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

    public static IDescramblerFactory For(DriverConfiguration configuration, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Tuner?.Backend is not TunerBackend.Dvb)
        {
            return NoDescrambling.Instance;
        }

        return Probe(logger);
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

    private ConditionalAccessCard TheCard()
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

        return ConditionalAccessCard.Open(service, identity => Volatile.Write(ref cardInHand, identity));
    }
}
