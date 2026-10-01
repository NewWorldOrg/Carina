using System.Diagnostics;

using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProgrammeEnvironmentTests
{
    [Fact(DisplayName = "nothing this process was given is handed on to the one it starts")]
    public void NothingThisProcessWasGivenIsHandedOnToTheOneItStarts()
    {
        string? held = Environment.GetEnvironmentVariable("CARINA_DB_CONNECTION");
        Environment.SetEnvironmentVariable("CARINA_DB_CONNECTION", "Host=db;Password=hunter2");

        try
        {
            ProcessStartInfo start = AnotherProgramme.Describe("ffmpeg", []);

            Assert.DoesNotContain("CARINA_DB_CONNECTION", start.Environment.Keys, StringComparer.Ordinal);
            Assert.Equal(["PATH"], start.Environment.Keys.Order(StringComparer.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CARINA_DB_CONNECTION", held);
        }
    }

    [Fact(DisplayName = "the search path a started programme gets is written down, not inherited")]
    public void TheSearchPathAStartedProgrammeGetsIsWrittenDownNotInherited()
    {
        string? searched = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", "/somewhere/else");

        try
        {
            Assert.Equal(AnotherProgramme.SearchedIn, AnotherProgramme.Describe("ffmpeg", []).Environment["PATH"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", searched);
        }
    }
}
