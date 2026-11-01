using System.Net;
using System.Reflection;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;

namespace Carina.Driver.Tests;

public sealed class DriverIdentityTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheGreetingCarriesTheVersionTheRepositoryDeclares()
    {
        string declared = typeof(DriverHello).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

        Assert.Equal(declared, DriverGreeting.ForThisProcess(descrambling: false).Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", DriverGreeting.Version);
    }

    [Fact]
    public void TheGreetingCarriesWhenTheDriverStartedAndTheNameItWasGiven()
    {
        DriverHello hello = DriverGreeting.ForThisProcess(descrambling: false, "living-room", Start);

        Assert.Equal(Start, hello.StartedAt);
        Assert.Equal("living-room", hello.Name);
    }

    [Fact]
    public void ADriverGivenNoNameSaysNone()
    {
        Assert.Null(DriverGreeting.ForThisProcess(descrambling: false).Name);
    }

    [Fact]
    public async Task TheHealthAnswerCarriesTheNameFromTheConfiguration()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshape: configuration => configuration with { Name = "living-room" }
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(
            DriverEndpoints.Health,
            new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token
        );
        DriverHello? hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("living-room", hello?.Name);
        Assert.NotNull(hello?.StartedAt);
        Assert.Equal(DriverGreeting.Version, hello?.Version);
    }
}

public sealed class DriverNameConfigurationTests
{
    private static string Configuration(string name) => $$"""
        {
          "name": {{name}},
          "socketPath": "/run/carina/driver.sock",
          "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
          "tuner": { "backend": "fake" },
          "devices": [{ "id": "fake-terrestrial", "kind": "terrestrial" }]
        }
        """;

    [Fact]
    public void TheNameIsReadFromTheConfiguration()
    {
        DriverConfigurationResult result = DriverConfigurationReader.Read(Configuration("\"living-room\""));

        Assert.DoesNotContain(result.Problems, problem => problem.StartsWith("name", StringComparison.Ordinal));
        Assert.Equal("living-room", result.Configuration?.Name);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    [InlineData("\" padded \"")]
    [InlineData("\"tab\\there\"")]
    [InlineData("\"a-name-much-longer-than-anyone-would-call-a-machine\"")]
    public void ANameThatCannotBeShownAsItIsIsRefused(string name)
    {
        DriverConfigurationResult result = DriverConfigurationReader.Read(Configuration(name));

        Assert.Contains(result.Problems, problem => problem.StartsWith("name:", StringComparison.Ordinal));
    }

    [Fact]
    public void ADriverWithoutANameIsAccepted()
    {
        DriverConfigurationResult result = DriverConfigurationReader.Read("""
            {
              "socketPath": "/run/carina/driver.sock",
              "outputRoots": [{ "name": "primary", "path": "/srv/recordings" }],
              "tuner": { "backend": "fake" },
              "devices": [{ "id": "fake-terrestrial", "kind": "terrestrial" }]
            }
            """);

        Assert.DoesNotContain(result.Problems, problem => problem.StartsWith("name", StringComparison.Ordinal));
        Assert.Null(result.Configuration?.Name);
    }

    [Fact]
    public void TheNameSurvivesTheLedgerBeingWrittenBack()
    {
        DriverConfiguration configuration = DriverConfigurationReader.Read(Configuration("\"living-room\"")).Configuration!;

        Assert.Equal(
            "living-room",
            DriverConfigurationReader.Parse(DriverConfigurationWriter.Serialize(configuration))?.Name
        );
    }
}
