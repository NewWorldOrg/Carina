namespace Carina.Architecture.Tests;

public sealed class CaptionFontRuleSelfCheckTests
{
    private const string Sound = """
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS denpa-font
        ARG DENPA_FONT_VERSION=v3.2
        ARG DENPA_FONT_SHA256=c20f77bdad691223c50c43609df4b4e87916bfdc7d29fe7c4690f2fd6e72414c
        RUN curl -fsSLO "https://github.com/NewWorldOrg/denpa-font/releases/download/${DENPA_FONT_VERSION}/denpa-font.ttf" \
            && curl -fsSLO "https://github.com/NewWorldOrg/denpa-font/releases/download/${DENPA_FONT_VERSION}/SHA256SUMS" \
            && grep ' denpa-font.ttf$' SHA256SUMS | sha256sum -c - \
            && echo "${DENPA_FONT_SHA256}  denpa-font.ttf" | sha256sum -c - \
            && cp denpa-font.ttf /out/fonts/denpa-font/

        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS develop
        COPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/
        RUN apt-get install -y fontconfig fonts-noto-cjk \
            && fc-cache -f

        FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base

        FROM runtime-base AS runtime
        COPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/
        RUN apt-get install -y fontconfig fonts-noto-cjk \
            && fc-cache -f
        """;

    [Fact]
    public void ASoundDockerfileReportsNothing()
    {
        Assert.Empty(CaptionFontRules.Violations(Sound));
        Assert.Equal("v3.2", CaptionFontRules.Version(Sound));
    }

    [Fact]
    public void DetectsAVersionThatIsNotARelease()
    {
        string dockerfile = Sound.Replace("ARG DENPA_FONT_VERSION=v3.2", "ARG DENPA_FONT_VERSION=main", StringComparison.Ordinal);

        Assert.Equal(["the font's version is not pinned as ARG DENPA_FONT_VERSION=vX.Y."], CaptionFontRules.Violations(dockerfile));
    }

    [Fact]
    public void DetectsASha256ThatIsNotOne()
    {
        string dockerfile = Sound.Replace(
            "ARG DENPA_FONT_SHA256=c20f77bdad691223c50c43609df4b4e87916bfdc7d29fe7c4690f2fd6e72414c",
            "ARG DENPA_FONT_SHA256=c20f77bd",
            StringComparison.Ordinal);

        Assert.Equal(["the font's sha256 is not pinned as ARG DENPA_FONT_SHA256."], CaptionFontRules.Violations(dockerfile));
    }

    [Fact]
    public void DetectsAFontCheckedAgainstOnlyOneOfTheTwo()
    {
        string withoutTheSums = Sound.Replace(
            "    && grep ' denpa-font.ttf$' SHA256SUMS | sha256sum -c - \\\n",
            string.Empty,
            StringComparison.Ordinal);
        string withoutThePin = Sound.Replace(
            "    && echo \"${DENPA_FONT_SHA256}  denpa-font.ttf\" | sha256sum -c - \\\n",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Equal(["the font is not checked against the SHA256SUMS of the release."], CaptionFontRules.Violations(withoutTheSums));
        Assert.Equal(["the font is not checked against the pinned sha256."], CaptionFontRules.Violations(withoutThePin));
    }

    [Fact]
    public void DetectsAFontFetchedFromSomewhereOtherThanTheRelease()
    {
        string dockerfile = Sound.Replace(
            "https://github.com/NewWorldOrg/denpa-font/releases/download/${DENPA_FONT_VERSION}/denpa-font.ttf",
            "https://example.invalid/denpa-font.ttf",
            StringComparison.Ordinal);

        Assert.Equal(
            [
                "the font is not fetched from the release the version names.",
                "the font is fetched from somewhere other than the release: https://example.invalid/denpa-font.ttf.",
            ],
            CaptionFontRules.Violations(dockerfile));
    }

    [Fact]
    public void DetectsAStageThatBuildsTheCacheBeforeTheFontIsThere()
    {
        string dockerfile = Sound.Replace(
            "FROM runtime-base AS runtime\nCOPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/\nRUN apt-get install -y fontconfig fonts-noto-cjk \\\n    && fc-cache -f",
            "FROM runtime-base AS runtime\nRUN apt-get install -y fontconfig fonts-noto-cjk \\\n    && fc-cache -f\nCOPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/",
            StringComparison.Ordinal);

        Assert.Equal(["runtime: the font is not installed before the font cache is built."], CaptionFontRules.Violations(dockerfile));
    }

    [Fact]
    public void DetectsAStageWithoutTheFallbackFace()
    {
        string dockerfile = Sound.Replace(
            "FROM mcr.microsoft.com/dotnet/sdk:10.0 AS develop\nCOPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/\nRUN apt-get install -y fontconfig fonts-noto-cjk \\",
            "FROM mcr.microsoft.com/dotnet/sdk:10.0 AS develop\nCOPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/\nRUN apt-get install -y fontconfig \\",
            StringComparison.Ordinal);

        Assert.Equal(["develop: the fallback face's package is not installed."], CaptionFontRules.Violations(dockerfile));
    }

    [Fact]
    public void DetectsAMissingStage()
    {
        string dockerfile = Sound.Replace("FROM runtime-base AS runtime\n", "FROM runtime-base AS final\n", StringComparison.Ordinal);

        Assert.Equal(["runtime: there is no such stage."], CaptionFontRules.Violations(dockerfile));
    }
}
