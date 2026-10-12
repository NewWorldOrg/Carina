namespace Carina.Domain.DataBroadcast;

/// <summary>
/// How an application named in an AIT is to be treated when a document asks for it.
/// </summary>
public enum HybridcastControlCode
{
    Autostart = 1,

    Present = 2,

    Kill = 4,
}

/// <summary>
/// One pair of an application's boundary: the sixteen bits of what the application is permitted, and the addresses
/// the pair applies to, none meaning every address.
/// </summary>
public sealed record HybridcastPermission(int Bitmap, IReadOnlyList<string> ManagedUrls);

/// <summary>
/// A Hybridcast application an AIT names that this receiver can run: its organisation and application identifiers,
/// its control code, the address it is entered at, the profile it declares where it declares one, and the pairs of
/// its boundary, none where the AIT carries no boundary at all.
/// </summary>
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
