using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

/// <summary>
/// The image fetches the face the captions are drawn with from one fixed release, checks it against the
/// release's own SHA256SUMS and against the sha256 the Dockerfile pins, and installs it beside the fallback
/// face in both stages that draw captions before the font cache is built.
/// </summary>
public static class CaptionFontRules
{
    public const string Face = "Denpa Font";

    public const string FallbackFace = "Noto Sans CJK JP";

    public const string FallbackPackage = "fonts-noto-cjk";

    public const string FontFile = "denpa-font.ttf";

    public const string Sums = "SHA256SUMS";

    public const string Release = "https://github.com/NewWorldOrg/denpa-font/releases/download/${DENPA_FONT_VERSION}/";

    public const string Installed = "COPY --from=denpa-font /out/fonts/ /usr/local/share/fonts/";

    public const string CacheBuilt = "fc-cache -f";

    public static readonly IReadOnlyList<string> StagesThatDrawCaptions = ["develop", "runtime"];

    public static string Dockerfile { get; } = Path.Combine(RepositoryLayout.Root, "Dockerfile");

    private static readonly Regex VersionArgument = new(
        @"^ARG DENPA_FONT_VERSION=(?<value>\S+)\s*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Sha256Argument = new(
        @"^ARG DENPA_FONT_SHA256=(?<value>\S+)\s*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ReleaseVersion = new(@"^v\d+\.\d+$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Sha256 = new(@"^[0-9a-f]{64}$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Stage = new(
        @"^FROM\s+\S+\s+AS\s+(?<name>\S+)\s*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex FetchesTheFontElsewhere = new(
        @"https?://[^\s""]*denpa[^\s""]*",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    public static string? Version(string dockerfile)
        => Argument(dockerfile, VersionArgument);

    public static string? PinnedSha256(string dockerfile)
        => Argument(dockerfile, Sha256Argument);

    public static IReadOnlyList<string> Violations(string dockerfile)
    {
        ArgumentNullException.ThrowIfNull(dockerfile);

        List<string> violations = [];

        string? version = Version(dockerfile);
        if (version is null || !ReleaseVersion.IsMatch(version))
        {
            violations.Add("the font's version is not pinned as ARG DENPA_FONT_VERSION=vX.Y.");
        }

        string? sha256 = PinnedSha256(dockerfile);
        if (sha256 is null || !Sha256.IsMatch(sha256))
        {
            violations.Add("the font's sha256 is not pinned as ARG DENPA_FONT_SHA256.");
        }

        if (!dockerfile.Contains(Release + FontFile, StringComparison.Ordinal))
        {
            violations.Add("the font is not fetched from the release the version names.");
        }

        if (!dockerfile.Contains(Release + Sums, StringComparison.Ordinal))
        {
            violations.Add("the SHA256SUMS of that release is not fetched.");
        }

        if (!dockerfile.Contains($"grep ' {FontFile}$' {Sums} | sha256sum -c -", StringComparison.Ordinal))
        {
            violations.Add("the font is not checked against the SHA256SUMS of the release.");
        }

        if (!dockerfile.Contains($"echo \"${{DENPA_FONT_SHA256}}  {FontFile}\" | sha256sum -c -", StringComparison.Ordinal))
        {
            violations.Add("the font is not checked against the pinned sha256.");
        }

        foreach (Match fetched in FetchesTheFontElsewhere.Matches(dockerfile))
        {
            if (!fetched.Value.StartsWith(Release, StringComparison.Ordinal))
            {
                violations.Add($"the font is fetched from somewhere other than the release: {fetched.Value}.");
            }
        }

        foreach (string stage in StagesThatDrawCaptions)
        {
            string? body = StageBody(dockerfile, stage);

            if (body is null)
            {
                violations.Add($"{stage}: there is no such stage.");
                continue;
            }

            int installed = body.IndexOf(Installed, StringComparison.Ordinal);
            int cached = body.IndexOf(CacheBuilt, StringComparison.Ordinal);

            if (installed < 0 || cached < 0 || cached < installed)
            {
                violations.Add($"{stage}: the font is not installed before the font cache is built.");
            }

            if (!body.Contains(FallbackPackage, StringComparison.Ordinal))
            {
                violations.Add($"{stage}: the fallback face's package is not installed.");
            }
        }

        return violations;
    }

    private static string? Argument(string dockerfile, Regex argument)
    {
        ArgumentNullException.ThrowIfNull(dockerfile);

        Match match = argument.Match(dockerfile);

        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? StageBody(string dockerfile, string name)
    {
        MatchCollection stages = Stage.Matches(dockerfile);

        for (int i = 0; i < stages.Count; i++)
        {
            if (stages[i].Groups["name"].Value != name)
            {
                continue;
            }

            int from = stages[i].Index + stages[i].Length;
            int to = i + 1 < stages.Count ? stages[i + 1].Index : dockerfile.Length;

            return dockerfile[from..to];
        }

        return null;
    }
}
