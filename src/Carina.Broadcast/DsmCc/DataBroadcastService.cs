namespace Carina.Broadcast.DsmCc;

public sealed class DataBroadcastService
{
    internal DataBroadcastService(IReadOnlyList<DataBroadcastStream> streams)
    {
        Streams = streams;
        Entry = streams.FirstOrDefault(stream => stream.IsEntry);
    }

    public IReadOnlyList<DataBroadcastStream> Streams { get; }

    public DataBroadcastStream? Entry { get; }

    public bool IsCarried => Entry is not null;
}
