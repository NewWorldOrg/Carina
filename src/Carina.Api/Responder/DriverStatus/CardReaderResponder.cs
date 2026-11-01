using Carina.Contracts;

namespace Carina.Api.Responder.DriverStatus;

public sealed record CardReaderSlotResponder(string Name, CardState State, bool InUse);

public sealed record CardReaderResponder(
    CardReaderUse Use,
    CardState State,
    CardProblem? Problem,
    IReadOnlyList<CardReaderSlotResponder> Readers,
    string? LenderHostName,
    int? LeasesOut,
    DateTimeOffset? LastAnsweredAt,
    DateTimeOffset? CheckedAt)
{
    public static CardReaderResponder Of(CardReaderDto reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return new CardReaderResponder(
            reader.Use,
            reader.State,
            reader.Problem,
            [.. reader.Readers.Select(slot => new CardReaderSlotResponder(slot.Name, slot.State, slot.InUse))],
            reader.LenderHostName,
            reader.LeasesOut,
            reader.LastAnsweredAt,
            reader.CheckedAt);
    }
}
