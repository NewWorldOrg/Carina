using Carina.Driver.Ipc;

using Microsoft.Extensions.Configuration;

namespace Carina.Driver.Tests;

public sealed class TcpBindingGateTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(pair => KeyValuePair.Create(pair.Key, (string?)pair.Value)))
            .Build();

    private static string? Nothing(string name) => null;

    [Fact]
    public void APlainDriverBindsNoTcpPort()
    {
        Assert.Empty(TcpBindingGate.Inspect(Settings(), [], Nothing));
    }

    [Fact]
    public void TheUrlVariableIsNamed()
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(
            Settings(),
            [],
            name => name is TcpBindingGate.UrlsVariable ? "http://0.0.0.0:8080" : null
        );

        Assert.Contains(
            findings,
            finding =>
                finding.Contains(TcpBindingGate.UrlsVariable, StringComparison.Ordinal)
                && finding.Contains("http://0.0.0.0:8080", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AnEmptyUrlVariableIsNotAFinding()
    {
        Assert.Empty(
            TcpBindingGate.Inspect(
                Settings(),
                [],
                name => name is TcpBindingGate.UrlsVariable ? string.Empty : null
            )
        );
    }

    [Theory]
    [InlineData(TcpBindingGate.HttpPortsVariable)]
    [InlineData(TcpBindingGate.HttpsPortsVariable)]
    public void APortTheImageHandedDownIsNamed(string variable)
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(
            Settings(),
            [],
            name => name == variable ? "8080" : null
        );

        Assert.Contains(
            findings,
            finding =>
                finding.Contains(variable, StringComparison.Ordinal)
                && finding.Contains("8080", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("http_ports")]
    [InlineData("https_ports")]
    public void APortSettingIsNamed(string setting)
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(Settings((setting, "8080")), [], Nothing);

        Assert.Contains(
            findings,
            finding => finding.Contains(setting, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AnEmptiedPortVariableIsNotAFinding()
    {
        Assert.Empty(
            TcpBindingGate.Inspect(
                Settings((TcpBindingGate.HttpPortsVariable, string.Empty)),
                [],
                name => name is TcpBindingGate.HttpPortsVariable ? string.Empty : null
            )
        );
    }

    [Fact]
    public void TheUrlArgumentIsNamed()
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(Settings(), ["--urls", "http://0.0.0.0:8080"], Nothing);

        Assert.Contains(findings, finding => finding.Contains("--urls", StringComparison.Ordinal));
    }

    [Fact]
    public void TheJoinedUrlArgumentIsNamed()
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(Settings(), ["--urls=http://0.0.0.0:8080"], Nothing);

        Assert.Contains(
            findings,
            finding => finding.Contains("--urls=http://0.0.0.0:8080", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AnArgumentThatMerelyStartsTheSameWayIsLeftAlone()
    {
        Assert.Empty(TcpBindingGate.Inspect(Settings(), ["--urlsomething"], Nothing));
    }

    [Fact]
    public void TheUrlSettingIsNamedWhateverPutItThere()
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(
            Settings((TcpBindingGate.UrlsSetting, "http://[::]:5000")),
            [],
            Nothing
        );

        Assert.Contains(
            findings,
            finding => finding.Contains("http://[::]:5000", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void EveryKestrelEndpointIsNamed()
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(
            Settings(
                ("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5000"),
                ("Kestrel:Endpoints:Https:Url", "https://0.0.0.0:5001")
            ),
            [],
            Nothing
        );

        Assert.Contains(
            findings,
            finding => finding.Contains("Kestrel:Endpoints:Http", StringComparison.Ordinal)
        );
        Assert.Contains(
            findings,
            finding => finding.Contains("Kestrel:Endpoints:Https", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void EveryFindingSaysWhyTheDriverRefuses()
    {
        IReadOnlyList<string> findings = TcpBindingGate.Inspect(
            Settings(("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5000")),
            ["--urls=http://0.0.0.0:8080"],
            name => name is TcpBindingGate.UrlsVariable ? "http://0.0.0.0:9090" : null
        );

        Assert.NotEmpty(findings);
        Assert.All(
            findings,
            finding => Assert.Contains("never binds a TCP port", finding, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TheVariablesTheEntrypointDropsAreTheOnesTheGateNames()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docker", "entrypoint.sh"));
        int opened = Array.FindIndex(
            lines,
            line => line.StartsWith("drop_web_server_variables()", StringComparison.Ordinal)
        );

        Assert.True(opened >= 0, "docker/entrypoint.sh no longer has a drop_web_server_variables function.");

        int looped = Array.FindIndex(lines, opened, line => line.Contains(Loop, StringComparison.Ordinal));

        Assert.True(looped >= 0, $"docker/entrypoint.sh no longer names the variables it drops after '{Loop}'.");

        string named = lines[looped][(lines[looped].IndexOf(Loop, StringComparison.Ordinal) + Loop.Length)..];

        Assert.Equal(
            TcpBindingGate.Variables,
            named[..named.IndexOf(';')]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        );
    }

    private const string Loop = "for name in ";

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Carina.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
