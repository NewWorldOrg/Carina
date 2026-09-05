namespace Carina.Api.Logos;

public static class LogoSurfaces
{
    public const string Tag = "services";

    public const string TheLogoIsCalled = "getServiceLogo";

    public const string TheLogoOfAStation =
        "The logo a station broadcasts, as a PNG. It answers 404 where the station has none, and carries an "
        + "entity tag so a screen showing every channel asks for each logo once.";
}
