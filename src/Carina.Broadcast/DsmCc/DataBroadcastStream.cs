namespace Carina.Broadcast.DsmCc;

public sealed record DataBroadcastStream(int Pid, int ComponentTag, BxmlInfo? Bxml, bool CarriesCarousels)
{
    public bool HasEntryComponentTag => CarriesCarousels && ComponentTag == DataBroadcastStreams.EntryComponentTag;
}
