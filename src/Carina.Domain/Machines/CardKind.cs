namespace Carina.Domain.Machines;

/// <summary>
/// Which kind of card this machine encodes on. A machine has one kind, and every path that uses the card,
/// the encode queue, live, playback and the probe that asks whether it works, uses the same one.
/// </summary>
public enum CardKind
{
    Vaapi = 1,

    Nvenc = 2,
}

public static class CardKinds
{
    public static CardKind Named(CardKind card)
        => Enum.IsDefined(card)
            ? card
            : throw new ArgumentOutOfRangeException(nameof(card), card, "A card is one of the kinds named here.");

    /// <summary>
    /// The one card the two settings name between them: the card either names, VAAPI when neither names a card,
    /// and a refusal when they name different ones, because a machine is taken to have one kind of card.
    /// </summary>
    public static CardKind Agreed(CardKind? encodes, CardKind? lives)
    {
        CardKind? encodesOn = encodes is { } first ? Named(first) : null;
        CardKind? livesOn = lives is { } second ? Named(second) : null;

        if (encodesOn is { } one && livesOn is { } other && one != other)
        {
            throw new ArgumentException(
                $"Encoding names the {one} card and live names the {other} card; a machine has one kind of card, so the two name the same one or one of them names the processor.",
                nameof(lives));
        }

        return encodesOn ?? livesOn ?? CardKind.Vaapi;
    }
}
