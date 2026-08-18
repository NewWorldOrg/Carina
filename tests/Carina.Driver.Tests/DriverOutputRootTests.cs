using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class DriverOutputRootTests
{
    private const string Complete = """
        {
          "socketPath": "/run/carina/driver.sock",
          "outputRoots": [
            { "name": "primary", "path": "/srv/recordings" }
          ],
          "shutdownGraceHours": 6,
          "tuner": { "backend": "fake" },
          "devices": [
            { "id": "adapter0", "kind": "terrestrial", "enabled": true }
          ]
        }
        """;

    private static IReadOnlyList<string> Problems(string json) =>
        DriverConfigurationReader.Read(json).Problems;

    private static string WithRoots(string roots) =>
        Complete.Replace(
            """
            "outputRoots": [
                { "name": "primary", "path": "/srv/recordings" }
              ]
            """.Trim(),
            roots
        );

    [Fact]
    public void ADeclaredRootIsReadAsANameAndAPath()
    {
        DriverConfiguration? configuration = DriverConfigurationReader.Read(Complete).Configuration;

        Assert.NotNull(configuration);
        OutputRootSettings root = Assert.Single(configuration.OutputRoots!);
        Assert.Equal("primary", root.Name);
        Assert.Equal("/srv/recordings", root.Path);
    }

    [Fact]
    public void ADriverWithNoRootCannotRecordAndSaysSo()
    {
        Assert.Contains(
            Problems(WithRoots("""
                "outputRoots": []
                """)),
            problem => problem.StartsWith("outputRoots:")
        );
    }

    [Fact]
    public void ARootThatIsMissingEntirelyIsAFinding()
    {
        Assert.Contains(
            Problems(Complete.Replace("\"outputRoots\"", "\"outputRoot\"")),
            problem => problem.StartsWith("outputRoots:")
        );
    }

    [Fact]
    public void ANullRootIsAFindingAndNotACrash()
    {
        Assert.Contains(
            Problems(WithRoots("""
                "outputRoots": [ null ]
                """)),
            problem => problem.StartsWith("outputRoots[0]:")
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("/srv/recordings")]
    [InlineData("two words")]
    public void ARootNameThatIsNotANameIsAFinding(string name)
    {
        Assert.Contains(
            Problems(WithRoots($$"""
                "outputRoots": [
                    { "name": "{{name}}", "path": "/srv/recordings" }
                  ]
                """)),
            problem => problem.StartsWith("outputRoots[0].name:")
        );
    }

    [Theory]
    [InlineData("srv/recordings")]
    [InlineData("/srv/../etc")]
    [InlineData("")]
    public void ARootPathThatIsNotAbsoluteIsAFinding(string path)
    {
        Assert.Contains(
            Problems(WithRoots($$"""
                "outputRoots": [
                    { "name": "primary", "path": "{{path}}" }
                  ]
                """)),
            problem => problem.StartsWith("outputRoots[0].path:")
        );
    }

    [Fact]
    public void TwoRootsUnderOneNameAreAFinding()
    {
        Assert.Contains(
            Problems(WithRoots("""
                "outputRoots": [
                    { "name": "primary", "path": "/srv/one" },
                    { "name": "primary", "path": "/srv/two" }
                  ]
                """)),
            problem => problem.StartsWith("outputRoots[1].name:")
        );
    }

    [Fact]
    public void AnUnknownRootSettingIsNamed()
    {
        Assert.Contains(
            Problems(WithRoots("""
                "outputRoots": [
                    { "name": "primary", "path": "/srv/recordings", "quota": 100 }
                  ]
                """)),
            problem => problem.StartsWith("outputRoots[0].quota:")
        );
    }

    [Fact]
    public void TheSettingThisReplacedIsNoLongerAccepted()
    {
        Assert.Contains(
            Problems(
                Complete.Replace(
                    "\"socketPath\"",
                    "\"recordingsDirectory\": \"/srv/recordings\",\n  \"socketPath\""
                )
            ),
            problem => problem.StartsWith("recordingsDirectory:")
        );
    }

    [Fact]
    public void OnlyADeclaredNameResolvesToAPath()
    {
        DriverConfiguration configuration = DriverConfigurationReader.Read(Complete).Configuration!;

        Assert.True(configuration.TryResolveOutputRoot("primary", out string? path));
        Assert.Equal("/srv/recordings", path);
    }

    [Theory]
    [InlineData("secondary")]
    [InlineData("/srv/recordings")]
    [InlineData("../../etc")]
    [InlineData("")]
    [InlineData(null)]
    public void ANameTheDriverNeverDeclaredResolvesToNothing(string? name)
    {
        DriverConfiguration configuration = DriverConfigurationReader.Read(Complete).Configuration!;

        Assert.False(configuration.TryResolveOutputRoot(name, out string? path));
        Assert.Null(path);
    }

    [Fact]
    public void TheGroupThatOwnsTheSocketHasAnAgreedDefault()
    {
        DriverConfiguration configuration = DriverConfigurationReader.Read(Complete).Configuration!;

        Assert.Equal(DriverConfiguration.DefaultSocketGroupId, configuration.SocketGroupId);
        Assert.Equal(10001, DriverConfiguration.DefaultSocketGroupId);
        Assert.Equal("carina", DriverConfiguration.SocketGroupName);
    }

    [Fact]
    public void TheGroupThatOwnsTheSocketCanBeSetForTheHostItRunsOn()
    {
        DriverConfiguration configuration = DriverConfigurationReader
            .Read(Complete.Replace("\"socketPath\"", "\"socketGroupId\": 3000,\n  \"socketPath\""))
            .Configuration!;

        Assert.Equal(3000, configuration.SocketGroupId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ASocketGroupThatWouldWidenAccessIsAFinding(int groupId)
    {
        Assert.Contains(
            Problems(
                Complete.Replace(
                    "\"socketPath\"",
                    $"\"socketGroupId\": {groupId},\n  \"socketPath\""
                )
            ),
            problem => problem.StartsWith("socketGroupId:")
        );
    }

    [Fact]
    public void ALiveSessionRunsForAsLongAsTheConfigurationSays()
    {
        DriverConfiguration configuration = DriverConfigurationReader
            .Read(
                Complete.Replace("\"socketPath\"", "\"liveSessionMinutes\": 30,\n  \"socketPath\"")
            )
            .Configuration!;

        Assert.Equal(30, configuration.LiveSessionMinutes);
        Assert.Equal(
            DriverConfiguration.DefaultLiveSessionMinutes,
            DriverConfigurationReader.Read(Complete).Configuration!.LiveSessionMinutes
        );
    }

    [Fact]
    public void AWalkSessionLengthIsReadAndHasItsOwnDefault()
    {
        DriverConfiguration configuration = DriverConfigurationReader
            .Read(Complete.Replace("\"socketPath\"", "\"walkSessionMinutes\": 15,\n  \"socketPath\""))
            .Configuration!;

        Assert.Equal(15, configuration.WalkSessionMinutes);
        Assert.Equal(
            DriverConfiguration.DefaultWalkSessionMinutes,
            DriverConfigurationReader.Read(Complete).Configuration!.WalkSessionMinutes
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void AWalkSessionLengthOutsideItsRangeIsAFinding(int minutes)
    {
        Assert.Contains(
            Problems(
                Complete.Replace(
                    "\"socketPath\"",
                    $"\"walkSessionMinutes\": {minutes},\n  \"socketPath\""
                )
            ),
            problem => problem.StartsWith("walkSessionMinutes:")
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void ALiveSessionLengthOutsideItsRangeIsAFinding(int minutes)
    {
        Assert.Contains(
            Problems(
                Complete.Replace(
                    "\"socketPath\"",
                    $"\"liveSessionMinutes\": {minutes},\n  \"socketPath\""
                )
            ),
            problem => problem.StartsWith("liveSessionMinutes:")
        );
    }
}
