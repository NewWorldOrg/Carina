using System.Reflection;
using System.Xml.Linq;

using Carina.Api.Common;

namespace Carina.Api.Tests.Unit;

public sealed class DeclaredVersionTests
{
    [Fact]
    public void TheRevisionABuildAppendsIsNotPartOfTheVersion()
    {
        Assert.Equal("2.4.1", DeclaredVersion.WrittenAs("2.4.1+0123456789abcdef0123456789abcdef01234567"));
    }

    [Fact]
    public void AVersionCarryingNoRevisionIsTakenAsItIs()
    {
        Assert.Equal("2.4.1", DeclaredVersion.WrittenAs("2.4.1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+0123456789abcdef0123456789abcdef01234567")]
    public void ABuildThatDeclaredNoVersionIsRefusedRatherThanAnsweredAsBlank(string? informational)
    {
        Assert.Throws<InvalidOperationException>(() => DeclaredVersion.WrittenAs(informational));
    }

    [Fact]
    public void TheVersionTheApplicationAnswersWithIsTheOneDeclaredForTheWholeRepository()
    {
        Assert.Equal(
            WhatTheBuildPropsDeclare(),
            DeclaredVersion.Of(typeof(DeclaredVersion).Assembly));
    }

    [Fact]
    public void EveryProcessBuiltFromThisRepositoryCarriesTheSameVersion()
    {
        Assembly[] built =
        [
            typeof(DeclaredVersion).Assembly,
            typeof(Carina.Contracts.DriverProtocol).Assembly,
            typeof(Carina.Domain.Recordings.RecordingId).Assembly,
        ];

        Assert.Equal(
            [WhatTheBuildPropsDeclare()],
            built.Select(DeclaredVersion.Of).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string WhatTheBuildPropsDeclare()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Carina.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string? declared = XDocument
            .Load(Path.Combine(directory.FullName, "Directory.Build.props"))
            .Descendants("Version")
            .SingleOrDefault()
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(declared));

        return declared!;
    }
}
