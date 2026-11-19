namespace Carina.Domain.DataBroadcast;

public enum HybridcastControlCode
{
    Autostart = 1,

    Present = 2,

    Kill = 4,
}

public sealed record HybridcastPermission(int Bitmap, IReadOnlyList<string> ManagedUrls);

public sealed record HybridcastApplication(
    uint OrgId,
    ushort AppId,
    HybridcastControlCode ControlCode,
    Uri EntryUrl,
    int? Profile,
    IReadOnlyList<HybridcastPermission> Boundary)
{
    public bool IsStartedBy(uint orgId, ushort appId)
        => OrgId == orgId && AppId == appId && ControlCode is HybridcastControlCode.Autostart;

    public HybridcastBoundary Reach() => HybridcastBoundary.Of(EntryUrl, Boundary);
}
