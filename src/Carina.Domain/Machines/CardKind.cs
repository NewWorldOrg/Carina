namespace Carina.Domain.Machines;

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
