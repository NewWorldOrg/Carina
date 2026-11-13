using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Unit.Infra.Encodings;

[Trait("Category", "Unit")]
public sealed class LocalEncodeArtefactWeigherTests : IDisposable
{
    private static readonly OutputRoot Encodes = new("encodes");

    private readonly string room = Directory.CreateTempSubdirectory("carina-artefact-weigh").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "an artefact on the disk weighs what the disk says")]
    public void AnArtefactOnTheDiskWeighsWhatTheDiskSays()
    {
        EncodeFileName name = Named();
        File.WriteAllBytes(Path.Combine(room, name.Value), new byte[4096]);

        Assert.Equal(4096, Weigher().Weigh(Encodes, name));
    }

    [Fact(DisplayName = "an artefact that is not there is unread rather than empty")]
    public void AnArtefactThatIsNotThereIsUnreadRatherThanEmpty() => Assert.Null(Weigher().Weigh(Encodes, Named()));

    [Fact(DisplayName = "an artefact under a root this process does not hold is unread, and the root recordings are read from is not looked under")]
    public void AnArtefactUnderARootThisProcessDoesNotHoldIsUnread()
    {
        EncodeFileName name = Named();
        File.WriteAllBytes(Path.Combine(room, name.Value), new byte[4096]);

        Assert.Null(Weigher().Weigh(new OutputRoot("primary"), name));
        Assert.Null(Weigher().Weigh(new OutputRoot("bulk"), name));
    }

    [Fact(DisplayName = "a directory where the artefact should be is unread rather than empty")]
    public void ADirectoryWhereTheArtefactShouldBeIsUnread()
    {
        EncodeFileName name = Named();
        Directory.CreateDirectory(Path.Combine(room, name.Value));

        Assert.Null(Weigher().Weigh(Encodes, name));
    }

    private static EncodeFileName Named() => EncodeFileName.Artefact(RecordingId.New(), EncodeProfileId.New());

    private LocalEncodeArtefactWeigher Weigher()
        => new(
            new EncodePlaces(
                new IntegritySettings { OutputRoots = [new StorageRootPath(new OutputRoot("primary"), room)] },
                new EncodeSettings { OutputRoots = [new StorageRootPath(Encodes, room)] }),
            NullLogger<LocalEncodeArtefactWeigher>.Instance);
}
