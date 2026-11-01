using System.Text.RegularExpressions;

using Carina.Contracts;
using Carina.Driver.CardLending;
using Carina.Driver.Configuration;

namespace Carina.Driver.Descrambling;

/// <summary>
/// Something that can say how its card stands without speaking to the card.
/// </summary>
public interface ICardReaderReport
{
    CardReaderDto Inspect();
}

public static partial class CardReaderNames
{
    /// <summary>
    /// The reader's name as it may be shown: pcsc-lite adds the reader's serial number in parentheses, the interface in
    /// brackets and the slot as two numbers at the end, and the serial names one unit.
    /// </summary>
    public static string Shown(string listed) =>
        Spaces().Replace(Slot().Replace(Enclosed().Replace(listed, " "), string.Empty), " ").Trim();

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Enclosed();

    [GeneratedRegex(@"\s+[0-9A-Fa-f]{2}\s+[0-9A-Fa-f]{2}\s*$")]
    private static partial Regex Slot();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}

public static class CardReaders
{
    /// <summary>
    /// How the card of a driver stands. A DVB driver that reads no card has no smart-card library to read it with, since
    /// the configuration cannot otherwise leave it without one.
    /// </summary>
    public static CardReaderDto Inspect(
        IDescramblerFactory cards,
        DriverConfiguration configuration,
        CardLender? lender,
        TimeProvider clock
    )
    {
        CardReaderDto report = cards switch
        {
            ICardReaderReport reported => reported.Inspect(),
            _ when cards.Unscrambles => new CardReaderDto { Use = CardReaderUse.Local, State = CardState.Unspecified },
            _ when configuration.Tuner?.Backend is TunerBackend.Dvb => new CardReaderDto
            {
                Use = CardReaderUse.Local,
                State = CardState.Unresponsive,
                Problem = CardProblem.LibraryMissing,
            },
            _ => new CardReaderDto { Use = CardReaderUse.None, State = CardState.NotUsed },
        };

        return report with { LeasesOut = lender?.LeasesOut, CheckedAt = report.CheckedAt ?? clock.GetUtcNow() };
    }
}

/// <summary>
/// Passes a card through, writing down each time it answers an ECM.
/// </summary>
internal sealed class NoticedCard(IConditionalAccess card, Action answered) : IConditionalAccess
{
    public int CaSystemId => card.CaSystemId;

    public EcmAnswer Answer(ReadOnlySpan<byte> ecmBody)
    {
        EcmAnswer answer = card.Answer(ecmBody);
        answered();

        return answer;
    }

    public void Dispose() => card.Dispose();
}

/// <summary>
/// When something last answered and last failed to, as ticks so a stream's thread and a request can share them.
/// </summary>
internal sealed class AnswerTimes(TimeProvider clock)
{
    private long answered;

    private long failed;

    public void Answered() => Interlocked.Exchange(ref answered, clock.GetUtcNow().UtcTicks);

    public void Failed() => Interlocked.Exchange(ref failed, clock.GetUtcNow().UtcTicks);

    public DateTimeOffset? LastAnswered => At(Interlocked.Read(ref answered));

    public bool FailedSinceItAnswered => Interlocked.Read(ref failed) > Interlocked.Read(ref answered);

    private static DateTimeOffset? At(long ticks) => ticks is 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
}
