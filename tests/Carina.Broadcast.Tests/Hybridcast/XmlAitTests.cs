using System.Text;

using Carina.Broadcast.Hybridcast;

namespace Carina.Broadcast.Tests.Hybridcast;

public sealed class XmlAitTests
{
    private const string Isdb = "urn:arib:isdb:2012";

    private const string Mhp = "urn:dvb:mhp:2009";

    [Fact(DisplayName = "BR-BV-007: an application is read for its identifiers, control code, entry, profile and boundary")]
    public void AnApplicationIsReadWhole()
    {
        AitApplication read = Assert.Single(XmlAit.Read(Ait(Application()))!);

        Assert.Equal(16u, read.OrgId);
        Assert.Equal((ushort)1, read.AppId);
        Assert.Equal(AitControlCode.Autostart, read.ControlCode);
        Assert.Equal("http://hc.station.example.jp/p/index.html", read.EntryUrl);
        Assert.Equal(1, read.Profile);
        AitPermission permission = Assert.Single(read.Boundary);
        Assert.Equal(0x1fff, permission.Bitmap);
        Assert.Equal(["http://station.example.jp", "https://station.example.jp"], permission.ManagedUrls);
    }

    [Theory(DisplayName = "BR-BV-007: the identifiers are read in whichever namespace they are written in, as decimal numbers")]
    [InlineData("isdb")]
    [InlineData("mhp")]
    public void TheIdentifiersAreReadInEitherNamespace(string prefix)
    {
        string identifier = $"<isdb:applicationIdentifier><{prefix}:orgId>18</{prefix}:orgId><{prefix}:appId>10</{prefix}:appId></isdb:applicationIdentifier>";

        AitApplication read = Assert.Single(XmlAit.Read(Ait(Application(identifier: identifier)))!);

        Assert.Equal(18u, read.OrgId);
        Assert.Equal((ushort)10, read.AppId);
    }

    [Theory(DisplayName = "BR-BV-007: an application whose identifiers are missing, not numbers or name organisation 0 is left out")]
    [InlineData("<isdb:applicationIdentifier><isdb:appId>1</isdb:appId></isdb:applicationIdentifier>")]
    [InlineData("<isdb:applicationIdentifier><isdb:orgId>10</isdb:orgId></isdb:applicationIdentifier>")]
    [InlineData("<isdb:applicationIdentifier><isdb:orgId>0x10</isdb:orgId><isdb:appId>1</isdb:appId></isdb:applicationIdentifier>")]
    [InlineData("<isdb:applicationIdentifier><isdb:orgId>0</isdb:orgId><isdb:appId>1</isdb:appId></isdb:applicationIdentifier>")]
    [InlineData("<isdb:applicationIdentifier><isdb:orgId>16</isdb:orgId><isdb:appId>70000</isdb:appId></isdb:applicationIdentifier>")]
    public void AnApplicationWithoutUsableIdentifiersIsLeftOut(string identifier)
    {
        Assert.Empty(XmlAit.Read(Ait(Application(identifier: identifier)))!);
    }

    [Fact(DisplayName = "BR-BV-007: an empty application location makes the base the entry")]
    public void AnEmptyLocationMakesTheBaseTheEntry()
    {
        AitApplication read = Assert.Single(XmlAit.Read(Ait(Application(location: "<isdb:applicationLocation></isdb:applicationLocation>")))!);

        Assert.Equal("http://hc.station.example.jp/", read.EntryUrl);
    }

    [Fact(DisplayName = "BR-BV-007: the base, the extension and the location are joined into the entry")]
    public void TheBaseTheExtensionAndTheLocationAreJoined()
    {
        string transport = "<isdb:applicationTransport xsi:type=\"isdb:HTTPTransportType\"><isdb:URLBase>https://hc.station.example.jp/</isdb:URLBase><isdb:URLExtension>app/</isdb:URLExtension></isdb:applicationTransport>";

        AitApplication read = Assert.Single(XmlAit.Read(Ait(Application(transport: transport)))!);

        Assert.Equal("https://hc.station.example.jp/app/p/index.html", read.EntryUrl);
    }

    [Theory(DisplayName = "BR-BV-007: an application carried any way but HTTP, or whose entry is not http or https, is left out")]
    [InlineData("<isdb:applicationTransport xsi:type=\"isdb:DCTransportType\"><isdb:URLBase>http://hc.station.example.jp/</isdb:URLBase></isdb:applicationTransport>")]
    [InlineData("<isdb:applicationTransport xsi:type=\"isdb:HTTPTransportType\"><isdb:URLBase>ftp://hc.station.example.jp/</isdb:URLBase></isdb:applicationTransport>")]
    [InlineData("<isdb:applicationTransport xsi:type=\"isdb:HTTPTransportType\"><isdb:URLBase>/p/</isdb:URLBase></isdb:applicationTransport>")]
    [InlineData("")]
    public void AnApplicationNotCarriedOverHttpIsLeftOut(string transport)
    {
        Assert.Empty(XmlAit.Read(Ait(Application(transport: transport)))!);
    }

    [Theory(DisplayName = "BR-BV-007: only AUTOSTART, PRESENT and KILL are read, and PREFETCH or anything else leaves the application out")]
    [InlineData("AUTOSTART", AitControlCode.Autostart)]
    [InlineData("PRESENT", AitControlCode.Present)]
    [InlineData("KILL", AitControlCode.Kill)]
    [InlineData("PREFETCH", null)]
    [InlineData("DESTROY", null)]
    [InlineData("", null)]
    public void OnlyThreeControlCodesAreRead(string code, AitControlCode? expected)
    {
        IReadOnlyList<AitApplication> read = XmlAit.Read(Ait(Application(controlCode: code)))!;

        Assert.Equal(expected, read.SingleOrDefault()?.ControlCode);
    }

    [Theory(DisplayName = "BR-BV-007: profile 1 up to version 1.1.1 runs, anything later or of profile 2 is left out, and no version runs")]
    [InlineData(1, 1, 1, 1, true)]
    [InlineData(1, 1, 0, 9, true)]
    [InlineData(1, 1, 1, 2, false)]
    [InlineData(1, 2, 0, 0, false)]
    [InlineData(2, 1, 1, 1, false)]
    public void ProfileOneUpToOneOneOneRuns(int profile, int major, int minor, int micro, bool runs)
    {
        string version = $"<isdb:mhpVersion><mhp:profile>{profile}</mhp:profile><mhp:versionMajor>{major}</mhp:versionMajor><mhp:versionMinor>{minor}</mhp:versionMinor><mhp:versionMicro>{micro}</mhp:versionMicro></isdb:mhpVersion>";

        IReadOnlyList<AitApplication> read = XmlAit.Read(Ait(Application(version: version)))!;

        Assert.Equal(runs, read.Count == 1);
    }

    [Fact(DisplayName = "BR-BV-007: an application that names no version runs, with no profile")]
    public void AnApplicationThatNamesNoVersionRuns()
    {
        AitApplication read = Assert.Single(XmlAit.Read(Ait(Application(version: string.Empty)))!);

        Assert.Null(read.Profile);
    }

    [Fact(DisplayName = "BR-BV-007: an application of any kind but ISDB-HTML is left out")]
    public void AnApplicationOfAnotherKindIsLeftOut()
    {
        Assert.Empty(XmlAit.Read(Ait(Application(kind: "<isdb:type><isdb:IsdbApp>ISDB-BML</isdb:IsdbApp></isdb:type>")))!);
        Assert.Empty(XmlAit.Read(Ait(Application(kind: string.Empty)))!);
    }

    [Fact(DisplayName = "BR-BV-007: no boundary descriptor is no boundary entry, and a pair without managed addresses is kept as one")]
    public void NoDescriptorIsNoBoundaryEntry()
    {
        AitApplication without = Assert.Single(XmlAit.Read(Ait(Application(boundary: string.Empty)))!);
        AitApplication open = Assert.Single(XmlAit.Read(Ait(Application(
            boundary: "<isdb:applicationBoundaryAndPermissionDescriptor><isdb:boundaryAndPermission><isdb:permissionBitmap>1fff</isdb:permissionBitmap></isdb:boundaryAndPermission></isdb:applicationBoundaryAndPermissionDescriptor>")))!);

        Assert.Empty(without.Boundary);
        AitPermission pair = Assert.Single(open.Boundary);
        Assert.Equal(0x1fff, pair.Bitmap);
        Assert.Empty(pair.ManagedUrls);
    }

    [Fact(DisplayName = "BR-BV-008: a permission bitmap that is not sixteen bits of hexadecimal permits nothing")]
    public void AnUnreadableBitmapPermitsNothing()
    {
        AitApplication read = Assert.Single(XmlAit.Read(Ait(Application(
            boundary: "<isdb:applicationBoundaryAndPermissionDescriptor><isdb:boundaryAndPermission><isdb:permissionBitmap>1fffff</isdb:permissionBitmap><isdb:managedURL>http://station.example.jp</isdb:managedURL></isdb:boundaryAndPermission></isdb:applicationBoundaryAndPermissionDescriptor>")))!);

        Assert.Equal(0, Assert.Single(read.Boundary).Bitmap);
    }

    [Fact(DisplayName = "BR-BV-007: every application of the list is read, and one left out does not take the others with it")]
    public void EveryApplicationOfTheListIsRead()
    {
        string second = Application(identifier: "<isdb:applicationIdentifier><isdb:orgId>16</isdb:orgId><isdb:appId>2</isdb:appId></isdb:applicationIdentifier>", controlCode: "PRESENT");
        string dropped = Application(controlCode: "PREFETCH");

        IReadOnlyList<AitApplication> read = XmlAit.Read(Ait(Application() + dropped + second))!;

        Assert.Equal([(ushort)1, (ushort)2], read.Select(application => application.AppId));
    }

    [Theory(DisplayName = "BR-BV-007: a document that is not XML, or not the discovery chain in the ISDB namespace, is not an AIT")]
    [InlineData("<html><body>moved</body></html>")]
    [InlineData("not xml at all")]
    [InlineData("<ServiceDiscovery><ApplicationDiscovery><ApplicationList/></ApplicationDiscovery></ServiceDiscovery>")]
    [InlineData("<mhp:ServiceDiscovery xmlns:mhp=\"urn:dvb:mhp:2009\"><mhp:ApplicationDiscovery><mhp:ApplicationList/></mhp:ApplicationDiscovery></mhp:ServiceDiscovery>")]
    [InlineData("<isdb:ServiceDiscovery xmlns:isdb=\"urn:arib:isdb:2012\"><isdb:ApplicationList/></isdb:ServiceDiscovery>")]
    public void ADocumentOfAnotherShapeIsNotAnAit(string document)
    {
        Assert.Null(XmlAit.Read(Encoding.UTF8.GetBytes(document)));
    }

    [Fact(DisplayName = "BR-BV-007: a document larger than an AIT is allowed to be is not read")]
    public void ADocumentTooLargeIsNotRead()
    {
        byte[] padded = Ait(Application() + new string(' ', XmlAit.Largest));

        Assert.Null(XmlAit.Read(padded));
    }

    [Fact(DisplayName = "BR-BV-007: a document type declaration is refused rather than expanded")]
    public void ADocumentTypeDeclarationIsRefused()
    {
        string declared = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY e \"x\">]>" + Encoding.UTF8.GetString(Ait(Application()))[Encoding.UTF8.GetString(Ait(Application())).IndexOf("<isdb:ServiceDiscovery", StringComparison.Ordinal)..];

        Assert.Null(XmlAit.Read(Encoding.UTF8.GetBytes(declared)));
    }

    private static byte[] Ait(string applications)
        => Encoding.UTF8.GetBytes(
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<isdb:ServiceDiscovery xmlns:isdb=\"{Isdb}\" xmlns:mhp=\"{Mhp}\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + "<isdb:ApplicationDiscovery DomainName=\"station.example.jp\"><isdb:ApplicationList>"
            + applications
            + "</isdb:ApplicationList></isdb:ApplicationDiscovery></isdb:ServiceDiscovery>");

    private static string Application(
        string identifier = "<isdb:applicationIdentifier><isdb:orgId>16</isdb:orgId><isdb:appId>1</isdb:appId></isdb:applicationIdentifier>",
        string kind = "<isdb:type><isdb:IsdbApp>ISDB-HTML</isdb:IsdbApp></isdb:type>",
        string controlCode = "AUTOSTART",
        string version = "<isdb:mhpVersion><mhp:profile>1</mhp:profile><mhp:versionMajor>1</mhp:versionMajor><mhp:versionMinor>1</mhp:versionMinor><mhp:versionMicro>1</mhp:versionMicro></isdb:mhpVersion>",
        string transport = "<isdb:applicationTransport xsi:type=\"isdb:HTTPTransportType\"><isdb:URLBase>http://hc.station.example.jp/</isdb:URLBase></isdb:applicationTransport>",
        string location = "<isdb:applicationLocation>p/index.html</isdb:applicationLocation>",
        string boundary = "<isdb:applicationBoundaryAndPermissionDescriptor><isdb:boundaryAndPermission><isdb:permissionBitmap>1fff</isdb:permissionBitmap><isdb:managedURL>http://station.example.jp</isdb:managedURL><isdb:managedURL>https://station.example.jp</isdb:managedURL></isdb:boundaryAndPermission></isdb:applicationBoundaryAndPermissionDescriptor>")
        => "<isdb:Application>"
           + identifier
           + $"<isdb:applicationDescriptor>{kind}<isdb:controlCode>{controlCode}</isdb:controlCode><isdb:priority>ff</isdb:priority><isdb:version>00</isdb:version>{version}</isdb:applicationDescriptor>"
           + transport
           + location
           + boundary
           + "</isdb:Application>";
}
