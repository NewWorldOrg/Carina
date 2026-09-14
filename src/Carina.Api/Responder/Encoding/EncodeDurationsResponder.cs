using Carina.Domain.Encodings;

namespace Carina.Api.Responder.Encoding;

/// <summary>
/// How long the jobs that finished took. <c>averageSeconds</c> is unsaid until
/// <c>fewestToAverage</c> jobs have finished, because an average of one job is not an average; the
/// count and the window are answered either way, so a caller can say how far off an answer is
/// rather than reading a nought as an instant encode.
/// </summary>
public sealed record EncodeDurationsResponder(
    int Jobs,
    int FewestToAverage,
    int LookedAtAtMost,
    double? AverageSeconds,
    DateTime? From,
    DateTime? To)
{
    public const int Places = 3;

    public static EncodeDurationsResponder Of(EncodeSpells spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        return new EncodeDurationsResponder(
            spells.Counted,
            EncodeSpells.FewestToAverage,
            EncodeSpells.MostLookedAt,
            spells.Average is { } average ? Math.Round(average.TotalSeconds, Places) : null,
            spells.Oldest,
            spells.Newest);
    }
}
