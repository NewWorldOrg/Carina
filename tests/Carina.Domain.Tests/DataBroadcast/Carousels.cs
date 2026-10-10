using System.Text;

using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

internal static class Carousels
{
    public const int Entry = 0x40;

    public const int Other = 0x50;

    public static DataBroadcastEntry EntryOf(int service = 1024, bool autoStart = false)
        => new(new ServiceId(service), Entry, autoStart);

    public static CarouselSignal.Carried Carried(int service = 1024, bool autoStart = false)
        => new(EntryOf(service, autoStart), [Entry, Other]);

    public static CarouselSignal.CatalogUpdated Listing(int tag, params (int Id, int Version)[] modules)
        => Listing(tag, 1, [], modules);

    public static CarouselSignal.CatalogUpdated Listing(int tag, uint downloadId, int[] superseded, params (int Id, int Version)[] modules)
        => new(tag, downloadId, [.. modules.Select(module => new ListedModule(module.Id, module.Version, 100))], superseded);

    public static CarouselSignal.ModuleCompleted Completed(int tag, int moduleId, int version, string path = "startup.bml")
        => new(tag, moduleId, version, [Resource(path)]);

    public static CarouselResource Resource(string path, int bodyLength = 10)
        => new(path, "text/X-arib-bml", ResourceForm.Text, new byte[bodyLength]);

    public static EventMessage Event(int id, long firesAt)
        => new(1, id, 1, EventTiming.Npt, firesAt, Encoding.ASCII.GetBytes("event"));

    public static ModuleVersion Version(int tag, int moduleId, int version, long firstSeen, int bodyLength = 10)
        => new(tag, moduleId, version, firstSeen, firstSeen, [Resource("a.bml", bodyLength)]);
}
