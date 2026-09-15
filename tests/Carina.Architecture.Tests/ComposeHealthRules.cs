using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static class ComposeHealthRules
{
    public const string Driver = "driver";

    public const string App = "app";

    public static string ComposeFile { get; } = Path.Combine(RepositoryLayout.Root, "compose.yml");

    private static readonly Regex GeneralHttpClient = new(@"\b(curl|wget)\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex NamesTheDriver = new(@"^(-\s*)?['""]?driver['""]?:?$", RegexOptions.None, TimeSpan.FromSeconds(5));

    public static IReadOnlyList<string> Violations(string compose)
    {
        IReadOnlyList<string> services = Under(Lines(compose), "services");
        var violations = new List<string>();

        string driverCheck = HealthCheckOf(services, Driver);
        if (driverCheck.Length is 0)
        {
            violations.Add("driver: has no health check.");
        }
        else
        {
            if (!driverCheck.Contains("--probe", StringComparison.Ordinal))
            {
                violations.Add("driver: the health check does not ask the driver's own --probe.");
            }

            if (GeneralHttpClient.IsMatch(driverCheck))
            {
                violations.Add("driver: the health check reaches for a general HTTP client.");
            }
        }

        string appCheck = HealthCheckOf(services, App);
        if (appCheck.Length is 0)
        {
            violations.Add("app: has no health check.");
        }
        else if (!appCheck.Contains("/api/health", StringComparison.Ordinal))
        {
            violations.Add("app: the health check does not ask /api/health.");
        }

        foreach (string service in ServiceNames(compose).Where(name => name != Driver))
        {
            IReadOnlyList<string> dependencies = Under(Under(services, service), "depends_on");

            if (dependencies.Any(line => NamesTheDriver.IsMatch(line.Trim())))
            {
                violations.Add($"{service}: depends on the driver.");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> ServiceNames(string compose)
    {
        IReadOnlyList<string> services = Under(Lines(compose), "services");
        int level = LevelOf(services);

        return [.. services
            .Where(line => Meaningful(line) && Indentation(line) == level && line.TrimEnd().EndsWith(':'))
            .Select(line => line.Trim().TrimEnd(':'))];
    }

    private static string HealthCheckOf(IReadOnlyList<string> services, string service)
        => string.Join('\n', Under(Under(services, service), "healthcheck").Where(Meaningful));

    private static IReadOnlyList<string> Under(IReadOnlyList<string> lines, string key)
    {
        int level = LevelOf(lines);
        int start = -1;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];

            if (Meaningful(line) && Indentation(line) == level && line.Trim() == $"{key}:")
            {
                start = index + 1;
                break;
            }
        }

        if (start < 0)
        {
            return [];
        }

        var block = new List<string>();

        for (int index = start; index < lines.Count; index++)
        {
            string line = lines[index];

            if (Meaningful(line) && Indentation(line) <= level)
            {
                break;
            }

            block.Add(line);
        }

        return block;
    }

    private static int LevelOf(IReadOnlyList<string> lines)
        => lines.Where(Meaningful).Select(Indentation).DefaultIfEmpty(0).Min();

    private static string[] Lines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static bool Meaningful(string line)
        => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#');

    private static int Indentation(string line)
        => line.Length - line.TrimStart(' ').Length;
}
