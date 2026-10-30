using System.Net;
using System.Text.Json;

using Carina.Driver.CardLending;

namespace Carina.Driver.Configuration;

/// <summary>
/// What a card-lending setting must be before the driver opens a port for it or asks another driver for keys.
/// </summary>
public static class CardLendingRules
{
    public const string Section = "cardLending";

    public const int LeastPort = 1024;

    public const int MostPort = 65535;

    public const int MostLeases = 16;

    public const int MostRoundTripMilliseconds = 1000;

    public const int MostHostNames = 8;

    private static readonly string[] KnownKeys = ["lend", "borrow"];

    private static readonly string[] KnownLendKeys =
    [
        "address",
        "port",
        "hostNames",
        "hopLimit",
        "roundTripMilliseconds",
        "leases",
        "keyFile",
    ];

    private static readonly string[] KnownBorrowKeys = ["address", "port", "hostName", "hopLimit", "keyFile"];

    public static void FindUnknownKeys(JsonElement root, List<string> problems)
    {
        if (!root.TryGetProperty(Section, out JsonElement section) || section.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        Unknown(section, Section, KnownKeys, problems);

        if (section.TryGetProperty("lend", out JsonElement lend) && lend.ValueKind is JsonValueKind.Object)
        {
            Unknown(lend, $"{Section}.lend", KnownLendKeys, problems);
        }

        if (section.TryGetProperty("borrow", out JsonElement borrow) && borrow.ValueKind is JsonValueKind.Object)
        {
            Unknown(borrow, $"{Section}.borrow", KnownBorrowKeys, problems);
        }
    }

    public static IReadOnlyList<string> Problems(CardLendingSettings? settings)
    {
        if (settings is null)
        {
            return [];
        }

        List<string> problems = [];

        if (settings.Lend is not null && settings.Borrow is not null)
        {
            problems.Add($"{Section}: a driver lends its card or borrows one, not both.");
        }

        if (settings.Lend is LendSettings lend)
        {
            LendProblems(lend, problems);
        }

        if (settings.Borrow is BorrowSettings borrow)
        {
            BorrowProblems(borrow, problems);
        }

        return problems;
    }

    public static IReadOnlyList<string> FilesystemProblems(CardLendingSettings? settings)
    {
        List<string> problems = [];

        KeyProblem($"{Section}.lend", settings?.Lend?.KeyFile, settings?.Lend is not null, problems);
        KeyProblem($"{Section}.borrow", settings?.Borrow?.KeyFile, settings?.Borrow is not null, problems);

        return problems;
    }

    /// <summary>
    /// The lender's address and the names it answers to, the key read from its file, or the problems in the way.
    /// </summary>
    public static LendingPlan? Plan(LendSettings? lend, List<string> problems)
    {
        if (lend is null)
        {
            return null;
        }

        LendingKey? key = LendingKey.ReadFile(lend.KeyFile, out string? problem);
        if (key is null)
        {
            problems.Add($"{Section}.lend.keyFile: {problem}");

            return null;
        }

        return new LendingPlan(
            new IPEndPoint(IPAddress.Parse(lend.Address!), lend.Port),
            lend.HostNames ?? [],
            lend.HopLimit,
            lend.RoundTripMilliseconds is int most ? TimeSpan.FromMilliseconds(most) : null,
            lend.Leases,
            key
        );
    }

    public static CardLoan? Loan(BorrowSettings? borrow, List<string> problems)
    {
        if (borrow is null)
        {
            return null;
        }

        LendingKey? key = LendingKey.ReadFile(borrow.KeyFile, out string? problem);
        if (key is null)
        {
            problems.Add($"{Section}.borrow.keyFile: {problem}");

            return null;
        }

        return new CardLoan(
            new IPEndPoint(IPAddress.Parse(borrow.Address!), borrow.Port),
            borrow.HostName!,
            borrow.HopLimit,
            key
        );
    }

    private static void LendProblems(LendSettings lend, List<string> problems)
    {
        const string at = $"{Section}.lend";

        if (!IPAddress.TryParse(lend.Address, out IPAddress? address) || !(IsEveryAddress(address) || HomeNetwork.Holds(address)))
        {
            problems.Add(
                $"{at}.address: expected 0.0.0.0, :: or an address inside the household (loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, fc00::/7, fe80::/10), got '{lend.Address}'."
            );
        }

        PortProblem(at, lend.Port, problems);
        HopProblem(at, lend.HopLimit, problems);

        if (lend.Leases is < 1 or > MostLeases)
        {
            problems.Add($"{at}.leases: expected 1 to {MostLeases}, got {lend.Leases}.");
        }

        if (lend.RoundTripMilliseconds is int most && most is < 1 or > MostRoundTripMilliseconds)
        {
            problems.Add($"{at}.roundTripMilliseconds: expected 1 to {MostRoundTripMilliseconds}, or no setting at all, got {most}.");
        }

        IReadOnlyList<string> names = lend.HostNames ?? [];
        if (names.Count is 0 or > MostHostNames)
        {
            problems.Add($"{at}.hostNames: expected 1 to {MostHostNames} names the lender is reached by, got {names.Count}.");
        }

        for (int index = 0; index < names.Count; index++)
        {
            if (!IsHostName(names[index]))
            {
                problems.Add($"{at}.hostNames[{index}]: expected a host name or an address, got '{names[index]}'.");
            }
        }

        KeyFileProblem(at, lend.KeyFile, problems);
    }

    private static void BorrowProblems(BorrowSettings borrow, List<string> problems)
    {
        const string at = $"{Section}.borrow";

        if (!IPAddress.TryParse(borrow.Address, out IPAddress? address) || !HomeNetwork.Holds(address))
        {
            problems.Add(
                $"{at}.address: expected the lender's address inside the household (loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, fc00::/7, fe80::/10), got '{borrow.Address}'."
            );
        }

        PortProblem(at, borrow.Port, problems);
        HopProblem(at, borrow.HopLimit, problems);

        if (!IsHostName(borrow.HostName))
        {
            problems.Add($"{at}.hostName: expected the host name the lender answers to, got '{borrow.HostName}'.");
        }

        KeyFileProblem(at, borrow.KeyFile, problems);
    }

    private static bool IsEveryAddress(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    private static bool IsHostName(string? name) =>
        !string.IsNullOrEmpty(name) && Uri.CheckHostName(name) is not UriHostNameType.Unknown;

    private static void PortProblem(string at, int port, List<string> problems)
    {
        if (port is < LeastPort or > MostPort)
        {
            problems.Add($"{at}.port: expected {LeastPort} to {MostPort}, got {port}.");
        }
    }

    private static void HopProblem(string at, int hops, List<string> problems)
    {
        if (hops is < HopLimit.Least or > HopLimit.Most)
        {
            problems.Add($"{at}.hopLimit: expected {HopLimit.Least} to {HopLimit.Most}, got {hops}.");
        }
    }

    private static void KeyFileProblem(string at, string? keyFile, List<string> problems)
    {
        if (string.IsNullOrEmpty(keyFile) || !Path.IsPathRooted(keyFile))
        {
            problems.Add($"{at}.keyFile: expected an absolute path to the file holding the key, got '{keyFile}'.");
        }
    }

    private static void KeyProblem(string at, string? keyFile, bool asked, List<string> problems)
    {
        if (asked && LendingKey.ReadFile(keyFile, out string? problem) is null)
        {
            problems.Add($"{at}.keyFile: {problem}");
        }
    }

    private static void Unknown(JsonElement entry, string at, string[] known, List<string> problems)
    {
        foreach (JsonProperty property in entry.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                problems.Add($"{at}.{property.Name}: this driver has no such setting.");
            }
        }
    }
}
