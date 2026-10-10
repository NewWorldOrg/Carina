namespace Carina.Domain.DataBroadcast;

public enum DataBroadcastFault
{
    Unreadable = 1,

    TimedOut = 2,

    ClockUnread = 3,
}

/// <summary>
/// What reading a recording's file for its data broadcast came to: a record holding at least one module, none
/// because the recording carries no data broadcast or no module of it was put together, or a failure.
/// </summary>
public sealed record DataBroadcastTaking
{
    private DataBroadcastTaking(DataBroadcastRecord? record, DataBroadcastFault? fault, string note)
    {
        Record = record;
        Fault = fault;
        Note = note;
    }

    public DataBroadcastRecord? Record { get; }

    public DataBroadcastFault? Fault { get; }

    public string Note { get; }

    public int Modules => Record?.Modules ?? 0;

    public static DataBroadcastTaking Taken(DataBroadcastRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Modules is 0)
        {
            throw new ArgumentException("A record taken holds at least one module; one with none is missing.", nameof(record));
        }

        return new DataBroadcastTaking(record, null, string.Empty);
    }

    public static DataBroadcastTaking Missing() => new(null, null, string.Empty);

    public static DataBroadcastTaking Failed(DataBroadcastFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "Taking a record fails in one of the ways named here.");
        }

        ArgumentNullException.ThrowIfNull(note);

        return new DataBroadcastTaking(null, fault, note);
    }
}
