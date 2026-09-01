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
            Absent(logger, whyNot);

            return NoDescrambling.Instance;
        }

        try
        {
            CardDescrambler.Open(library).Dispose();
        }
        catch (DescramblingException error)
        {
            Absent(logger, error.Message);

            return NoDescrambling.Instance;
        }

        logger?.LogInformation(
            "A card answered the reader, so this driver unscrambles what it records and says so in its greeting."
        );

        return new Descramblers(library, logger);
    }

    public bool CardAnswered => true;

    public IDescrambler? Open()
    {
        try
        {
            return CardDescrambler.Open(library);
        }
        catch (DescramblingException error)
        {
            logger?.LogError(
                "The card answered when this driver started but does not now, so what this session records stays scrambled and its scrambled-packet count will say so: {Why}",
                error.Message
            );

            return null;
        }
    }

    private static void Absent(ILogger? logger, string why) =>
        logger?.LogWarning(
            "This driver does not unscramble and does not offer to: {Why} What it records stays as the tuner gave it, and its scrambled-packet count says how much of that was scrambled.",
            why
        );
}
