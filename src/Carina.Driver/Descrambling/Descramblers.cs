using Carina.Driver.Configuration;

using Microsoft.Extensions.Logging;

namespace Carina.Driver.Descrambling;

public sealed class Descramblers : IDescramblerFactory
{
    private readonly AribB25Library library;

    private readonly ILogger? logger;

    private Descramblers(AribB25Library library, ILogger? logger)
    {
        this.library = library;
        this.logger = logger;
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
        AribB25Library? library = AribB25Library.Load(out string whyNot);
        if (library is null)
        {
            logger?.LogWarning(
                "This driver does not unscramble and does not offer to: {Why} What it records stays as the tuner gave it, and its scrambled-packet count says how much of that was scrambled.",
                whyNot
            );

            return NoDescrambling.Instance;
        }

        Descramblers descramblers = new(library, logger);

        try
        {
            CardDescrambler.Open(library).Dispose();

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

    public IDescrambler? Open()
    {
        try
        {
            return CardDescrambler.Open(library);
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
}
