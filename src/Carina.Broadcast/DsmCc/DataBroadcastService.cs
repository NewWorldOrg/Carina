namespace Carina.Broadcast.DsmCc;

public sealed class DataBroadcastService
{
    internal DataBroadcastService(IReadOnlyList<DataBroadcastStream> streams, IReadOnlyList<DataBroadcastStreamDefect> defects)
    {
        Streams = streams;
        Defects = defects;
        Entry = streams.FirstOrDefault(stream => stream.IsEntry);
    }

    public IReadOnlyList<DataBroadcastStream> Streams { get; }

    public DataBroadcastStream? Entry { get; }

    public IReadOnlyList<DataBroadcastStreamDefect> Defects { get; }

    public bool IsCarried => Entry is not null;
}
