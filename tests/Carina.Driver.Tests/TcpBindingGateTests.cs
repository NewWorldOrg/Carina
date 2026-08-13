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
        var findings = TcpBindingGate.Inspect(
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

    [Fact]
    public void TheUrlArgumentIsNamed()
    {
        var findings = TcpBindingGate.Inspect(Settings(), ["--urls", "http://0.0.0.0:8080"], Nothing);

        Assert.Contains(findings, finding => finding.Contains("--urls", StringComparison.Ordinal));
    }

    [Fact]
    public void TheJoinedUrlArgumentIsNamed()
    {
        var findings = TcpBindingGate.Inspect(Settings(), ["--urls=http://0.0.0.0:8080"], Nothing);

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
        var findings = TcpBindingGate.Inspect(
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
        var findings = TcpBindingGate.Inspect(
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
        var findings = TcpBindingGate.Inspect(
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
}
