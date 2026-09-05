using Carina.Domain.Base;

namespace Carina.Domain.Machines;

/// <summary>
/// What this machine turned out to be able to do, asked once and answered the same way for
/// everyone: the live path and the encode path both read this instead of each working it out.
/// </summary>
public sealed record MachineCapabilities
{
    private MachineCapabilities(CardStanding card, IReadOnlyList<Faculty> faculties, string note)
    {
        Card = card;
        Faculties = faculties;
        Note = note;
    }

    public CardStanding Card { get; }

    public IReadOnlyList<Faculty> Faculties { get; }

    public string Note { get; }

    public bool CardIsUsable => CardStandings.IsUsable(Card);

    public bool Has(Faculty faculty) => Faculties.Contains(Machines.Faculties.Named(faculty));

    public static MachineCapabilities Of(CardStanding card, IEnumerable<Faculty> faculties, string note)
    {
        ArgumentNullException.ThrowIfNull(faculties);
        ArgumentNullException.ThrowIfNull(note);

        CardStanding standing = CardStandings.Named(card);
        Faculty[] can = [.. faculties.Select(Machines.Faculties.Named).Distinct().Order()];

        if (!CardStandings.IsUsable(standing) && can.Any(Machines.Faculties.NeedsTheCard))
        {
            throw new ArgumentException(
                $"A card that stands at {standing} cannot be listed as one this machine encodes on.",
                nameof(faculties));
        }

        return new MachineCapabilities(standing, can, ProgrammeNote.Of(note, ProgrammeNote.Longest));
    }
}
