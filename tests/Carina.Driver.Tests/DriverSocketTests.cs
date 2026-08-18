using System.Net.Sockets;

using Carina.Driver.Ipc;

namespace Carina.Driver.Tests;

public sealed class DriverSocketTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("carina-socket-").FullName;
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

    private Socket Bind(string path, bool listening)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));

        if (listening)
        {
            socket.Listen(1);
        }

        left.Add(socket);

        return socket;
    }

    [Fact]
    public void AFreePathIsLeftAlone()
    {
        string path = At("free.sock");

        DriverSocket.ClearStale(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ASocketNobodyAnswersOnIsRemoved()
    {
        string path = At("stale.sock");
        Bind(path, listening: false);
        Assert.True(File.Exists(path));

        DriverSocket.ClearStale(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ASocketAnotherDriverAnswersOnIsKept()
    {
        string path = At("live.sock");
        using Socket listener = Bind(path, listening: true);

        DriverSocketException refusal = Assert.Throws<DriverSocketException>(() => DriverSocket.ClearStale(path));

        Assert.Contains("another driver is already answering", refusal.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AnOrdinaryFileIsNeverDeleted()
    {
        string path = At("notasocket");
        File.WriteAllText(path, "something a person put here");

        DriverSocketException refusal = Assert.Throws<DriverSocketException>(() => DriverSocket.ClearStale(path));

        Assert.Contains("is not a socket", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("something a person put here", File.ReadAllText(path));
    }

    [Fact]
    public void ADirectoryIsNeverDeleted()
    {
        string path = At("adirectory");
        Directory.CreateDirectory(path);

        Assert.Throws<DriverSocketException>(() => DriverSocket.ClearStale(path));

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void APathTooLongForTheKernelIsNamed()
    {
        string path = Path.Combine(root, new string('p', 200));

        DriverSocketException refusal = Assert.Throws<DriverSocketException>(() => DriverSocket.ClearStale(path));

        Assert.Contains("at most 107 bytes", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABoundSocketTakesTheModeAndTheGroup()
    {
        string path = At("secured.sock");
        using Socket socket = Bind(path, listening: true);

        DriverSocket.Secure(path, (int)UnixFile.CurrentGroupId());

        UnixEntry entry = UnixFile.Inspect(path);
        Assert.Equal(UnixPathKind.Socket, entry.Kind);
        Assert.Equal(DriverSocket.RequiredPermissions, entry.Permissions);
        Assert.Equal(UnixFile.CurrentGroupId(), entry.GroupId);
        Assert.Equal("0660", UnixFile.Octal(entry.Permissions));
    }

    [Fact]
    public void ASocketThatDidNotTakeTheGroupIsRefused()
    {
        string path = At("ungrouped.sock");
        using Socket socket = Bind(path, listening: true);

        DriverSocketException refusal = Assert.Throws<DriverSocketException>(() => DriverSocket.Secure(path, -1));

        Assert.Contains("did not take 0660", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASocketThatIsNotThereCannotBeSecured()
    {
        DriverSocketException refusal = Assert.Throws<DriverSocketException>(
            () => DriverSocket.Secure(At("absent.sock"), (int)UnixFile.CurrentGroupId())
        );

        Assert.Contains("could not be set to 0660", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyASocketIsUnlinked()
    {
        string file = At("ordinary");
        File.WriteAllText(file, "x");

        Assert.False(DriverSocket.TryUnlink(file));
        Assert.True(File.Exists(file));

        string path = At("gone.sock");
        Bind(path, listening: false);

        Assert.True(DriverSocket.TryUnlink(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void UnlinkingWhatIsNotThereSaysSo()
    {
        Assert.False(DriverSocket.TryUnlink(At("absent.sock")));
    }
}
