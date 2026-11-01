using System.Text.Json.Serialization;

namespace Carina.Contracts;

/// <summary>
/// Which card a driver descrambles with: one in this machine's reader, one another driver lends, or none.
/// </summary>
[JsonConverter(typeof(CardReaderUseConverter))]
public enum CardReaderUse
{
    Unspecified = 0,

    Local = 1,

    Borrowed = 2,

    None = 3,
}

[JsonConverter(typeof(CardStateConverter))]
public enum CardState
{
    Unspecified = 0,

    Inserted = 1,

    Empty = 2,

    Unresponsive = 3,

    Borrowed = 4,

    NoReader = 5,

    NotUsed = 6,
}

/// <summary>
/// Why a card cannot be used, as a kind the reader can put in its own words.
/// </summary>
[JsonConverter(typeof(CardProblemConverter))]
public enum CardProblem
{
    Unspecified = 0,

    ServiceDown = 1,

    LibraryMissing = 2,

    CardSilent = 3,

    LenderUnreachable = 4,

    LenderRefused = 5,
}

public sealed record CardReaderSlotDto
{
    /// <summary>
    /// The reader's product name, without the serial number or the slot the smart-card service adds to it.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    public CardState State { get; init; }

    public bool InUse { get; init; }
}

/// <summary>
/// The card reader as a driver last looked at it. It names no card: the card ID, the answer to reset and the keys
/// stay inside the driver.
/// </summary>
public sealed record CardReaderDto
{
    private readonly IReadOnlyList<CardReaderSlotDto> readers = [];

    public CardReaderUse Use { get; init; }

    public CardState State { get; init; }

    public CardProblem? Problem { get; init; }

    public IReadOnlyList<CardReaderSlotDto> Readers
    {
        get => readers;
        init => readers = value ?? [];
    }

    /// <summary>
    /// The name the lender answers to, as the configuration gives it, when the card is borrowed.
    /// </summary>
    public string? LenderHostName { get; init; }

    /// <summary>
    /// How many tuners of other drivers this driver's card is lent to, when it lends.
    /// </summary>
    public int? LeasesOut { get; init; }

    public DateTimeOffset? LastAnsweredAt { get; init; }

    public DateTimeOffset? CheckedAt { get; init; }
}
