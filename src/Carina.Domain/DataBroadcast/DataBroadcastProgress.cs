namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The state of a recording's data broadcast record with the failures counted against it and, once made,
/// how many modules it holds.
/// </summary>
public sealed record DataBroadcastProgress
{
    public const int MostRetries = 3;

    public DataBroadcastProgress(DataBroadcastState state, int attempts, int? modules)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A record is in one of the five states named.");
        }

        if (state is DataBroadcastState.Made != modules is > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modules), modules, "A record that is made says how many modules it holds, and nothing else counts any.");
        }

        if (attempts < 0 || (attempts > 0 && state is not (DataBroadcastState.Failed or DataBroadcastState.Coming)))
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "Failures are counted only while a record has failed or is being tried again.");
        }

        if (state is DataBroadcastState.Failed && attempts == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "A record that failed has failed at least once.");
        }

        State = state;
        Attempts = attempts;
        Modules = modules;
    }

    public static DataBroadcastProgress NotYet { get; } = new(DataBroadcastState.None, 0, null);

    public DataBroadcastState State { get; }

    public int Attempts { get; }

    public int? Modules { get; }

    public bool IsRetryDue => State is DataBroadcastState.Failed && Attempts <= MostRetries;

    public DataBroadcastProgress RecordingEnded()
    {
        Expect(DataBroadcastState.None, "A record comes due once, when the recording ends.");

        return new DataBroadcastProgress(DataBroadcastState.Coming, 0, null);
    }

    public DataBroadcastProgress Taken(int modules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(modules);
        Expect(DataBroadcastState.Coming, "Only a record that is coming is taken.");

        return modules == 0
            ? new DataBroadcastProgress(DataBroadcastState.Missing, 0, null)
            : new DataBroadcastProgress(DataBroadcastState.Made, 0, modules);
    }

    public DataBroadcastProgress Failed()
    {
        Expect(DataBroadcastState.Coming, "Only a record that is coming fails.");

        return new DataBroadcastProgress(DataBroadcastState.Failed, Attempts + 1, null);
    }

    public DataBroadcastProgress Retried()
    {
        if (!IsRetryDue)
        {
            throw new InvalidOperationException($"A failed record is tried again at most {MostRetries} times.");
        }

        return new DataBroadcastProgress(DataBroadcastState.Coming, Attempts, null);
    }

    private void Expect(DataBroadcastState expected, string message)
    {
        if (State != expected)
        {
            throw new InvalidOperationException(message);
        }
    }
}
