namespace Carina.Contracts;

/// <summary>
/// The conditional-access card the driver last heard from: the CA system it belongs to, and its card ID as the six
/// bytes of the initial setting's answer written as twelve hexadecimal digits. It names the card, so it is never
/// written to a log; <see cref="ToString"/> leaves the ID out.
/// </summary>
public sealed record ConditionalAccessCardDto
{
    public int CaSystemId { get; init; }

    public string CardId { get; init; } = string.Empty;

    public override string ToString() => $"a card of CA system 0x{CaSystemId:X4}";
}
