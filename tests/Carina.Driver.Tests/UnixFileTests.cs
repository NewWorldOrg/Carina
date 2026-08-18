using System.Net.Sockets;

using Carina.Driver.Ipc;

namespace Carina.Driver.Tests;

public sealed class UnixFileTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("carina-unixfile-").FullName;
    private readonly List<Socket> left = [];

    public void Dispose()
    {
        foreach (Socket socket in left)
        {
            socket.Dispose();
        }

        Directory.Delete(root, recursive: true);
    }

    private string At(string name) => Path.Combine(root, name);

    private Socket LeaveBoundSocket(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        left.Add(socket);

        return socket;
    }

    [Fact]
    public void APathWithNothingOnItIsMissing()
    {
        Assert.Equal(UnixPathKind.Missing, UnixFile.Inspect(At("absent")).Kind);
    }

    [Fact]
    public void APathBelowAFileIsMissing()
    {
        string file = At("file");
        File.WriteAllText(file, "x");

        Assert.Equal(UnixPathKind.Missing, UnixFile.Inspect(Path.Combine(file, "below")).Kind);
    }

    [Fact]
    public void AnOrdinaryFileIsNotASocket()
    {
        string file = At("ordinary");
        File.WriteAllText(file, "x");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        UnixEntry entry = UnixFile.Inspect(file);

        Assert.Equal(UnixPathKind.Other, entry.Kind);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, entry.Permissions);
        Assert.Equal(UnixFile.CurrentUserId(), entry.UserId);
    }

    [Fact]
    public void ABoundSocketIsASocket()
    {
        string path = At("bound.sock");
        LeaveBoundSocket(path).Listen(1);

        Assert.Equal(UnixPathKind.Socket, UnixFile.Inspect(path).Kind);
    }

    [Fact]
    public void ASocketNobodyListensOnIsStillASocket()
    {
        string path = At("stale.sock");
        LeaveBoundSocket(path);

        Assert.Equal(UnixPathKind.Socket, UnixFile.Inspect(path).Kind);
    }

    [Fact]
    public void ADirectoryIsNotASocket()
    {
        string path = At("directory");
        Directory.CreateDirectory(path);

        Assert.NotEqual(UnixPathKind.Socket, UnixFile.Inspect(path).Kind);
    }

    [Fact]
    public void TheModeAndTheOwnerAreReadTogether()
    {
        string file = At("owned");
        File.WriteAllText(file, "x");
        File.SetUnixFileMode(
            file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
        );

        UnixEntry entry = UnixFile.Inspect(file);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            entry.Permissions
        );
        Assert.Equal(UnixFile.CurrentGroupId(), entry.GroupId);
    }

    [Fact]
    public void AFileIsGivenToAGroupThisProcessBelongsTo()
    {
        string file = At("given");
        File.WriteAllText(file, "x");

        Assert.True(UnixFile.TryGiveToGroup(file, UnixFile.CurrentGroupId(), out string? problem));
        Assert.Empty(problem);
        Assert.Equal(UnixFile.CurrentGroupId(), UnixFile.Inspect(file).GroupId);
    }

    [Fact]
    public void GivingAwayAFileThatIsNotThereSaysWhy()
    {
        Assert.False(UnixFile.TryGiveToGroup(At("absent"), UnixFile.CurrentGroupId(), out string? problem));
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void PermissionsAreSpelledInOctal()
    {
        Assert.Equal(
            "0660",
            UnixFile.Octal(
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupWrite
            )
        );
    }
}
