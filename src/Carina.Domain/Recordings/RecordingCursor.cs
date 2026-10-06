using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Carina.Domain.Recordings;

/// <summary>
/// The place in a list of recordings just after its last row: the value that row is ordered by and its
/// identifier, in the order and direction the list was asked for. It travels as an opaque string a query
/// string carries as it is.
/// </summary>
public sealed record RecordingCursor
{
    private const string Version = "1";

    private const char Between = ':';

    private RecordingCursor(RecordingSort sort, bool descending, DateTime key, RecordingId id)
    {
        Sort = sort;
        Descending = descending;
        Key = key;
        Id = id;
    }

    public RecordingSort Sort { get; }

    public bool Descending { get; }

    public DateTime Key { get; }

    public RecordingId Id { get; }

    public string Wire
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Join(
            Between,
            Version,
            ((int)Sort).ToString(CultureInfo.InvariantCulture),
            Descending ? "d" : "a",
            Key.Ticks.ToString(CultureInfo.InvariantCulture),
            Id.Wire)));

    public static RecordingCursor After(RecordingSort sort, bool descending, DateTime key, RecordingId id)
    {
        if (!Enum.IsDefined(sort))
        {
            throw new ArgumentOutOfRangeException(nameof(sort), sort, "A list of recordings is ordered by one of the sorts it names.");
        }

        ArgumentNullException.ThrowIfNull(id);

        return new RecordingCursor(sort, descending, DateTime.SpecifyKind(key, DateTimeKind.Utc), id);
    }

    /// <summary>
    /// The place just after a recording, in the order a query asked for.
    /// </summary>
    public static RecordingCursor Past(Recording recording, RecordingQuery query)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(query);

        return After(query.Sort, query.Descending, KeyOf(recording, query.Sort), recording.Id);
    }

    /// <summary>
    /// The value a recording is ordered by under a sort.
    /// </summary>
    public static DateTime KeyOf(Recording recording, RecordingSort sort)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return sort is RecordingSort.ProgrammeStartsAt ? recording.ProgrammeStartsAt : recording.StartedAtActual;
    }

    /// <summary>
    /// The place a string written by <see cref="Wire"/> names, or null for anything else.
    /// </summary>
    public static RecordingCursor? Read(string? wire)
    {
        if (Unwrapped(wire) is not { } inside)
        {
            return null;
        }

        string[] parts = inside.Split(Between);

        if (parts is not [Version, string sort, string direction, string ticks, string id])
        {
            return null;
        }

        return Sorted(sort) is { } named
            && Directed(direction) is { } descending
            && Ticked(ticks) is { } key
            && Identified(id) is { } recording
                ? new RecordingCursor(named, descending, key, recording)
                : null;
    }

    private static string? Unwrapped(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire) || !Base64Url.IsValid(wire))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(wire));
    }

    private static RecordingSort? Sorted(string sort)
        => int.TryParse(sort, NumberStyles.None, CultureInfo.InvariantCulture, out int named)
            && Enum.IsDefined((RecordingSort)named)
                ? (RecordingSort)named
                : null;

    private static bool? Directed(string direction) => direction switch
    {
        "d" => true,
        "a" => false,
        _ => null,
    };

    private static DateTime? Ticked(string ticks)
        => long.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out long read)
            && read <= DateTime.MaxValue.Ticks
                ? new DateTime(read, DateTimeKind.Utc)
                : null;

    private static RecordingId? Identified(string id)
        => Guid.TryParseExact(id, "N", out Guid read) && read != Guid.Empty ? new RecordingId(read) : null;
}
