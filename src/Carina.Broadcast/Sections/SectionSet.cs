namespace Carina.Broadcast.Sections;

public sealed class SectionSet
{
    private static readonly IReadOnlyList<Section> Nothing = [];

    private readonly Dictionary<int, Section> held = [];

    private int expectedCount;

    public SectionSet(int tableId, int tableIdExtension)
    {
        TableId = tableId;
        TableIdExtension = tableIdExtension;
    }

    public int TableId { get; }

    public int TableIdExtension { get; }

    public int? VersionNumber { get; private set; }

    public int HeldCount => held.Count;

    public bool IsComplete => expectedCount > 0 && held.Count == expectedCount;

    public bool Add(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (section.TableId != TableId || section.TableIdExtension != TableIdExtension || !section.IsCurrent)
        {
            return false;
        }

        var announced = section.LastSectionNumber + 1;

        if (section.SectionNumber >= announced)
        {
            return false;
        }

        var replacesWhatIsHeld = VersionNumber != section.VersionNumber;

        if (!replacesWhatIsHeld && held.ContainsKey(section.SectionNumber))
        {
            return false;
        }

        if (replacesWhatIsHeld)
        {
            held.Clear();
            VersionNumber = section.VersionNumber;
        }

        expectedCount = announced;
        held[section.SectionNumber] = section;

        return true;
    }

    public bool TryComplete(out IReadOnlyList<Section> sections)
    {
        if (!IsComplete)
        {
            sections = Nothing;

            return false;
        }

        sections = held.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray();

        return true;
    }

    public void Reset()
    {
        held.Clear();
        VersionNumber = null;
        expectedCount = 0;
    }
}
