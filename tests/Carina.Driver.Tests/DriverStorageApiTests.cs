using System.Net;

using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;

namespace Carina.Driver.Tests;

public sealed class DriverStorageApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    private static async Task<IReadOnlyList<StorageRootDto>> RootsOf(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            DriverEndpoints.Storage,
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<StorageRootDto>? roots = await DriverUnderTest.Read(
            response,
            DriverJson.Context.IReadOnlyListStorageRootDto
        );

        Assert.NotNull(roots);

        return roots;
    }

    [Fact]
    public async Task TheDriverNamesTheRootsItDeclaresAndNoOthers()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        IReadOnlyList<StorageRootDto> roots = await RootsOf(client);

        Assert.Equal(["primary"], roots.Select(root => root.Name));
    }

    [Fact]
    public async Task ARootSaysHowMuchRoomIsLeftAndWhetherItTakesAFile()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        StorageRootDto root = Assert.Single(await RootsOf(client));

        Assert.True(root.FreeBytes > 0, "The root reported no room at all on a running machine.");
        Assert.True(
            root.TotalBytes >= root.FreeBytes,
            $"The root has {root.FreeBytes} free of {root.TotalBytes}."
        );
        Assert.True(root.Writable);
    }

    [Fact]
    public void ARootTheDriverCannotReachIsNamedWithNoRoomAtAll()
    {
        string root = DriverUnderTest.NewRoot();

        try
        {
            IReadOnlyList<StorageRootDto> roots = StorageViews.Of(
                Declaring(
                    new OutputRootSettings("primary", root),
                    new OutputRootSettings("archive", Path.Combine(root, "not-mounted"))
                )
            );

            Assert.Equal(["primary", "archive"], roots.Select(candidate => candidate.Name));

            StorageRootDto missing = roots.Single(candidate => candidate.Name is "archive");

            Assert.False(missing.Writable);
            Assert.Equal(0, missing.FreeBytes);
            Assert.Equal(0, missing.TotalBytes);
            Assert.True(roots.Single(candidate => candidate.Name is "primary").Writable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ARootTheDriverCanReachButNotWriteToIsNamedWithTheRoomItHas()
    {
        string root = DriverUnderTest.NewRoot();
        string notADirectory = Path.Combine(root, "archive");

        File.WriteAllBytes(notADirectory, [0x47]);

        try
        {
            StorageRootDto blocked = Assert.Single(
                StorageViews.Of(Declaring(new OutputRootSettings("archive", notADirectory)))
            );

            Assert.False(blocked.Writable);
            Assert.True(
                blocked.FreeBytes > 0,
                "The root reported no room, so the writability answer came from never reaching it."
            );
            Assert.True(blocked.TotalBytes > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AProbeLeftBehindByADriverThatDiedIsSweptUpByTheNextOne()
    {
        string root = DriverUnderTest.NewRoot();
        string leftover = Path.Combine(root, $"{StorageViews.WriteProbePrefix}fromadriverthatdied");

        File.WriteAllBytes(leftover, [0x47]);

        try
        {
            StorageRootDto answered = Assert.Single(
                StorageViews.Of(Declaring(new OutputRootSettings("primary", root)))
            );

            Assert.True(answered.Writable);
            Assert.False(File.Exists(leftover));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ARecordingAlreadyOnDiskIsLeftAloneByTheProbeSweep()
    {
        string root = DriverUnderTest.NewRoot();
        string recording = Path.Combine(root, "k-90210.ts");

        File.WriteAllBytes(recording, [0x47]);

        try
        {
            Assert.True(
                Assert.Single(StorageViews.Of(Declaring(new OutputRootSettings("primary", root))))
                    .Writable
            );
            Assert.True(File.Exists(recording));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EveryOneOfManyAnswersAtOnceTellsTheTruthAboutAHealthyRoot()
    {
        string root = DriverUnderTest.NewRoot();

        try
        {
            DriverConfiguration configuration = Declaring(
                new OutputRootSettings("primary", root)
            );

            StorageRootDto[] answers = new StorageRootDto[16];

            Parallel.For(0, answers.Length, at => answers[at] = StorageViews.Of(configuration).Single());

            Assert.All(answers, answer => Assert.True(answer.Writable));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TheProbeThatAnswersWhetherARootIsWritableLeavesNothingBehind()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        string path = driver.Configuration.OutputRoots!.Single().Path!;

        await RootsOf(client);
        await RootsOf(client);

        Assert.Empty(Directory.GetFileSystemEntries(path));
    }

    [Theory]
    [InlineData("archive", HttpStatusCode.BadRequest, SessionRefusalTitles.UnknownOutputRoot)]
    [InlineData("/srv/recordings", HttpStatusCode.BadRequest, SessionRefusalTitles.Rejected)]
    [InlineData("../../etc", HttpStatusCode.BadRequest, SessionRefusalTitles.Rejected)]
    public async Task ANameTheStorageListingDoesNotCarryNeverBecomesADirectoryToWriteIn(
        string outputRoot,
        HttpStatusCode expected,
        string title
    )
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        IReadOnlyList<StorageRootDto> roots = await RootsOf(client);

        Assert.False(StorageRoots.Declares(roots, outputRoot));

        using HttpResponseMessage refused = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording(
                    "elsewhere",
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    outputRoot
                )
            ),
            Soon()
        );

        Assert.Equal(expected, refused.StatusCode);

        DriverProblem? problem = await DriverUnderTest.Read(
            refused,
            DriverJson.Context.DriverProblem
        );

        Assert.Equal(title, problem?.Title);
        Assert.Empty(Directory.GetFiles(driver.Configuration.OutputRoots!.Single().Path!));
    }

    [Fact]
    public async Task ARootTheStorageListingDoesCarryIsOneARecordingMayBeWrittenTo()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        IReadOnlyList<StorageRootDto> roots = await RootsOf(client);

        Assert.True(StorageRoots.Declares(roots, "primary"));

        using HttpResponseMessage created = await client.PostAsync(
            DriverEndpoints.Sessions,
            DriverUnderTest.Body(
                DriverUnderTest.Recording("here", DateTimeOffset.UtcNow.AddMinutes(5), "primary")
            ),
            Soon()
        );

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage stopped = await client.DeleteAsync(
            $"{DriverEndpoints.Session(SessionId.Parse("here"))}?reason=the test is over",
            Soon()
        );

        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
    }

    [Fact]
    public async Task ADriverThatAnswersForItsDisksSaysSoInItsGreeting()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.Health, Soon());

        DriverHello? hello = await DriverUnderTest.Read(response, DriverJson.Context.DriverHello);

        Assert.NotNull(hello);
        Assert.True(hello.Supports(DriverCapabilities.Storage));
    }

    [Fact]
    public void ARootWhoseSettingsAreHalfWrittenIsNotListedAtAll()
    {
        Assert.Empty(
            StorageViews.Of(
                Declaring(
                    new OutputRootSettings("primary", null),
                    new OutputRootSettings(null, "/srv")
                )
            )
        );
    }

    private static DriverConfiguration Declaring(params OutputRootSettings[] roots) =>
        new(
            "/run/carina/driver.sock",
            roots,
            6,
            new TunerSettings(TunerBackend.Fake),
            []
        );
}
