namespace Carina.Broadcast.DsmCc;

public sealed record DataBroadcastStream(int Pid, int ComponentTag, BxmlInfo? Bxml)
{
    public bool IsEntry => ComponentTag == DataBroadcastStreams.EntryComponentTag;
}
