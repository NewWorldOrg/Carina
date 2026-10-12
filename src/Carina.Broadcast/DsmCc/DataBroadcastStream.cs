namespace Carina.Broadcast.DsmCc;

/// <summary>
/// One data stream of a service: a stream marked as BML by its data component descriptor, which carries carousels
/// and may carry event messages beside them, or a stream in the range of data component tags left without that
/// descriptor, which carries only event messages and references of the programme time.
/// </summary>
public sealed record DataBroadcastStream(int Pid, int ComponentTag, BxmlInfo? Bxml, bool CarriesCarousels)
{
    public bool HasEntryComponentTag => CarriesCarousels && ComponentTag == DataBroadcastStreams.EntryComponentTag;
}
