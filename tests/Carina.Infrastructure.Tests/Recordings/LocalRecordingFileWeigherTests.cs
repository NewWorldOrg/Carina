using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class LocalRecordingFileWeigherTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string root = Directory.CreateTempSubdirectory("carina-weigh").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task AFileOnTheDiskWeighsWhatTheDiskSays()
    {
        RecordingFileName name = Named();
        await File.WriteAllBytesAsync(Path.Combine(root, name.Value), new byte[188 * 3], Cancel);

        Assert.Equal(564, await Weigher().WeighAsync(new OutputRoot("primary"), name, Cancel));
    }

    [Fact]
    public async Task AFileThatIsThereAndEmptyWeighsNothingRatherThanReadingAsUnread()
    {
        RecordingFileName name = Named();
        await File.WriteAllBytesAsync(Path.Combine(root, name.Value), [], Cancel);

        Assert.Equal(0, await Weigher().WeighAsync(new OutputRoot("primary"), name, Cancel));
    }

    [Fact]
    public async Task AFileThatIsNotThereIsUnreadRatherThanEmpty()
        => Assert.Null(await Weigher().WeighAsync(new OutputRoot("primary"), Named(), Cancel));

    [Fact]
    public async Task ARootNothingSaysWhereToFindIsUnreadRatherThanEmpty()
    {
        RecordingFileName name = Named();
        await File.WriteAllBytesAsync(Path.Combine(root, name.Value), new byte[188], Cancel);

        Assert.Null(await Weigher().WeighAsync(new OutputRoot("bulk"), name, Cancel));
    }

    [Fact]
    public async Task ADirectoryWhereTheFileShouldBeIsUnreadRatherThanEmpty()
    {
        RecordingFileName name = Named();
        Directory.CreateDirectory(Path.Combine(root, name.Value));

        Assert.Null(await Weigher().WeighAsync(new OutputRoot("primary"), name, Cancel));
    }

    private static RecordingFileName Named() => RecordingFileName.For(RecordingId.New(), ".ts");

    private LocalRecordingFileWeigher Weigher()
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(new OutputRoot("primary"), root)] },
            NullLogger<LocalRecordingFileWeigher>.Instance);
}
