using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodePlacesTests
{
    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Encodes = new("encodes");

    private static readonly IntegritySettings Mounts = new()
    {
        OutputRoots = [new StorageRootPath(Primary, "/srv/recordings")],
    };

    private static readonly EncodeSettings Held = new()
    {
        OutputRoots = [new StorageRootPath(Encodes, "/srv/encodes")],
    };

    [Fact(DisplayName = "BR-EV-001: a recording is read from under the root the driver wrote it into")]
    public void ARecordingIsReadFromUnderTheRootTheDriverWroteItInto()
    {
        var places = new EncodePlaces(Mounts, Held);

        Assert.Equal("/srv/recordings", places.WhereTheRecordingIs(Primary));
        Assert.Null(places.WhereTheRecordingIs(Encodes));
    }

    [Fact(DisplayName = "BR-EV-001: an artefact goes under a root this process holds for writing, and never under one it reads from")]
    public void AnArtefactGoesUnderARootThisProcessHoldsAndNeverUnderOneItReadsFrom()
    {
        var places = new EncodePlaces(Mounts, Held);

        Assert.Equal("/srv/encodes", places.WhereTheArtefactGoes(Encodes));
        Assert.Null(places.WhereTheArtefactGoes(Primary));
        Assert.Equal([Encodes], places.Held);
    }

    [Fact(DisplayName = "A-エンコード-024: work goes beside the artefact unless one directory is named for every root")]
    public void WorkGoesBesideTheArtefactUnlessOneDirectoryIsNamedForEveryRoot()
    {
        var beside = new EncodePlaces(Mounts, Held);
        var apart = new EncodePlaces(Mounts, Held with { WorkedIn = "/srv/encoding" });

        Assert.True(beside.WorksBesideTheArtefact);
        Assert.Equal("/srv/encodes", beside.WhereTheWorkGoes(Encodes));
        Assert.Null(beside.WhereTheWorkGoes(Primary));
        Assert.False(apart.WorksBesideTheArtefact);
        Assert.Equal("/srv/encoding", apart.WhereTheWorkGoes(Encodes));
    }

    [Fact]
    public void AProcessThatHoldsNoRootPlacesNothing()
    {
        var places = new EncodePlaces(Mounts, new EncodeSettings());

        Assert.Empty(places.Held);
        Assert.Null(places.WhereTheArtefactGoes(Primary));
        Assert.Equal("/srv/recordings", places.WhereTheRecordingIs(Primary));
    }
}
