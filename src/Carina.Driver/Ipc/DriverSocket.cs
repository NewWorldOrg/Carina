using System.Net.Sockets;
using System.Text;

using Carina.Driver.Configuration;

namespace Carina.Driver.Ipc;

public sealed class DriverSocketException(string message) : Exception(message);

public static class DriverSocket
{
    public const int MaxPathBytes = 107;

    public static readonly TimeSpan ProbePatience = TimeSpan.FromSeconds(2);

    public const UnixFileMode RequiredPermissions =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite;

    public static void ClearStale(string path)
    {
        int length = Encoding.UTF8.GetByteCount(path);
        if (length > MaxPathBytes)
        {
            throw new DriverSocketException(
                $"socketPath: a Unix socket path holds at most {MaxPathBytes} bytes, and '{path}' is {length}."
            );
        }

        UnixEntry entry = UnixFile.Inspect(path);

        switch (entry.Kind)
        {
            case UnixPathKind.Missing:
                return;

            case UnixPathKind.Socket when IsBeingServed(path):
                throw new DriverSocketException(
                    $"socketPath: another driver is already answering on '{path}'. One driver owns the tuners, so this one is not starting."
                );

            case UnixPathKind.Socket:
                Delete(path);

                return;

            case UnixPathKind.Other:
                throw new DriverSocketException(
                    $"socketPath: '{path}' exists and is not a socket, and the driver deletes nothing else. Move it aside or name another path."
                );

            default:
                throw new DriverSocketException(
                    $"socketPath: something is at '{path}' and the driver could not tell what it is, so it will not delete it. Look at the path by hand."
                );
        }
    }

    public static void Secure(string path, int groupId)
    {
        uint group = unchecked((uint)groupId);

        try
        {
            File.SetUnixFileMode(path, RequiredPermissions);
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new DriverSocketException(
                $"The socket at '{path}' could not be set to {UnixFile.Octal(RequiredPermissions)}: {error.Message}"
            );
        }

        if (!UnixFile.TryGiveToGroup(path, group, out string? problem))
        {
            throw new DriverSocketException(
                $"The socket at '{path}' could not be given to group {groupId} ('{DriverConfiguration.SocketGroupName}'): {problem}. This driver runs as uid {UnixFile.CurrentUserId()} gid {UnixFile.CurrentGroupId()}, and a process only hands a file to a group it belongs to."
            );
        }

        UnixEntry entry = UnixFile.Inspect(path);

        if (
            entry.Kind is not UnixPathKind.Socket
            || entry.Permissions != RequiredPermissions
            || entry.GroupId != group
        )
        {
            throw new DriverSocketException(
                $"The socket at '{path}' did not take {UnixFile.Octal(RequiredPermissions)} and group {groupId}; it reads back as {entry.Kind} {UnixFile.Octal(entry.Permissions)} group {entry.GroupId}. The driver will not leave a socket standing that it cannot vouch for."
            );
        }
    }

    public static bool TryUnlink(string path)
    {
        if (UnixFile.Inspect(path).Kind is not UnixPathKind.Socket)
        {
            return false;
        }

        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new DriverSocketException(
                $"socketPath: '{path}' is a socket nobody answers on, and it could not be removed: {error.Message}"
            );
        }
    }

    private static bool IsBeingServed(string path)
    {
        using var probe = new Socket(
            AddressFamily.Unix,
            SocketType.Stream,
            ProtocolType.Unspecified
        );

        Task connecting = probe.ConnectAsync(new UnixDomainSocketEndPoint(path));

        try
        {
            if (!connecting.Wait(ProbePatience))
            {
                _ = connecting.ContinueWith(
                    finished => _ = finished.Exception,
                    TaskScheduler.Default
                );

                return true;
            }

            return true;
        }
        catch (AggregateException gathered)
            when (gathered.InnerException is SocketException refusal)
        {
            return refusal.SocketErrorCode is not SocketError.ConnectionRefused;
        }
    }
}
