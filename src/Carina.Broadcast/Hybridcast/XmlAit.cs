using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Carina.Broadcast.Hybridcast;

public enum AitControlCode
{
    Autostart = 1,

    Present = 2,

    Kill = 4,
}

public sealed record AitPermission(int Bitmap, IReadOnlyList<string> ManagedUrls);

public sealed record AitApplication(
    uint OrgId,
    ushort AppId,
    AitControlCode ControlCode,
    string EntryUrl,
    int? Profile,
    IReadOnlyList<AitPermission> Boundary);

public static class XmlAit
{
    public const int Largest = 256 * 1024;

    public const string IsdbNamespace = "urn:arib:isdb:2012";

    private const string HtmlApplication = "ISDB-HTML";

    private const string HttpTransport = "HTTPTransportType";

    private const string SchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly XNamespace Isdb = IsdbNamespace;

    private static readonly Version LatestRunnable = new(1, 1, 1);

    private static readonly IReadOnlyDictionary<string, AitControlCode> ControlCodes = new Dictionary<string, AitControlCode>(StringComparer.Ordinal)
    {
        ["AUTOSTART"] = AitControlCode.Autostart,
        ["PRESENT"] = AitControlCode.Present,
        ["KILL"] = AitControlCode.Kill,
    };

    public static IReadOnlyList<AitApplication>? Read(ReadOnlySpan<byte> document)
    {
        if (document.Length > Largest || Parsed(document) is not { Root: { } root } || root.Name != Isdb + "ServiceDiscovery")
        {
            return null;
        }

        XElement[] lists = [.. root.Elements(Isdb + "ApplicationDiscovery").Elements(Isdb + "ApplicationList")];

        return lists.Length == 0
            ? null
            : [.. lists.Elements(Isdb + "Application").Select(Application).OfType<AitApplication>()];
    }

    private static XDocument? Parsed(ReadOnlySpan<byte> document)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = Largest,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        try
        {
            using MemoryStream stream = new(document.ToArray(), writable: false);
            using XmlReader reader = XmlReader.Create(stream, settings);

            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static AitApplication? Application(XElement application)
    {
        XElement? identifier = Child(application, "applicationIdentifier");
        XElement? descriptor = Child(application, "applicationDescriptor");

        if (descriptor is null
            || !uint.TryParse(Text(Child(identifier, "orgId")), NumberStyles.None, CultureInfo.InvariantCulture, out uint orgId)
            || orgId == 0
            || !ushort.TryParse(Text(Child(identifier, "appId")), NumberStyles.None, CultureInfo.InvariantCulture, out ushort appId)
            || Text(Child(Child(descriptor, "type"), "IsdbApp")) != HtmlApplication
            || !ControlCodes.TryGetValue(Text(Child(descriptor, "controlCode")) ?? string.Empty, out AitControlCode controlCode)
            || Runnable(Child(descriptor, "mhpVersion")) is not { } profile
            || Entry(application) is not { } entry)
        {
            return null;
        }

        return new AitApplication(orgId, appId, controlCode, entry, profile.Declared, Boundary(application));
    }

    private static DeclaredProfile? Runnable(XElement? version)
    {
        if (version is null)
        {
            return new DeclaredProfile(null);
        }

        int?[] parts = [.. new[] { "profile", "versionMajor", "versionMinor", "versionMicro" }.Select(name => Number(Child(version, name)))];

        if (parts is not [1, { } major, { } minor, { } micro])
        {
            return null;
        }

        return new Version(major, minor, micro) <= LatestRunnable ? new DeclaredProfile(1) : null;
    }

    private static string? Entry(XElement application)
    {
        XElement? transport = Children(application, "applicationTransport").FirstOrDefault(IsCarriedOverHttp);
        string location = Text(Child(application, "applicationLocation")) ?? string.Empty;

        if (transport is null || Text(Child(transport, "URLBase")) is not { Length: > 0 } urlBase)
        {
            return null;
        }

        string entry = urlBase + (Text(Child(transport, "URLExtension")) ?? string.Empty) + location;

        return Uri.TryCreate(entry, UriKind.Absolute, out Uri? read) && (read.Scheme == Uri.UriSchemeHttp || read.Scheme == Uri.UriSchemeHttps)
            ? entry
            : null;
    }

    private static bool IsCarriedOverHttp(XElement transport)
        => transport.Attribute(XName.Get("type", SchemaInstance))?.Value is { } type
           && type[(type.IndexOf(':', StringComparison.Ordinal) + 1)..] == HttpTransport;

    private static List<AitPermission> Boundary(XElement application)
        =>
        [
            .. Children(Child(application, "applicationBoundaryAndPermissionDescriptor"), "boundaryAndPermission")
                .Select(pair => new AitPermission(
                    Bitmap(Text(Child(pair, "permissionBitmap"))),
                    [.. Children(pair, "managedURL").Select(url => url.Value.Trim())])),
        ];

    private static int Bitmap(string? written)
        => written is { Length: > 0 and <= 4 }
           && int.TryParse(written, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int bitmap)
            ? bitmap
            : 0;

    private static int? Number(XElement? element)
        => int.TryParse(Text(element), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : null;

    private static XElement? Child(XElement? parent, string localName) => Children(parent, localName).FirstOrDefault();

    private static IEnumerable<XElement> Children(XElement? parent, string localName)
        => parent?.Elements().Where(element => element.Name.LocalName == localName) ?? [];

    private static string? Text(XElement? element) => element?.Value.Trim();

    private sealed record DeclaredProfile(int? Declared);
}
