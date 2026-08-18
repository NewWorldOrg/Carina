using System.Runtime.InteropServices;

namespace Carina.Driver.Ipc;

public enum UnixPathKind
{
    Missing,

    Socket,

    Other,

    Unreadable,
}

public readonly record struct UnixEntry(
    UnixPathKind Kind,
    UnixFileMode Permissions,
    uint UserId,
    uint GroupId
)
{
    public static readonly UnixEntry Missing = new(UnixPathKind.Missing, default, 0, 0);

    public static readonly UnixEntry Unreadable = new(UnixPathKind.Unreadable, default, 0, 0);
}

public static partial class UnixFile
{
    private const int StatBufferSize = 256;

    private const uint FileTypeMask = 0xF000;
    private const uint SocketFileType = 0xC000;
    private const uint PermissionMask = 0x0FFF;

    private const uint LeaveTheOwnerAlone = uint.MaxValue;

    private const int NoSuchEntry = 2;
    private const int NotADirectory = 20;

    public static uint CurrentUserId() => GetUserId();

    public static uint CurrentGroupId() => GetGroupId();

    public static UnixEntry Inspect(string path)
    {
        byte[] buffer = new byte[StatBufferSize];

        if (Stat(path, buffer) is not 0)
        {
            return Marshal.GetLastPInvokeError() is NoSuchEntry or NotADirectory
                ? UnixEntry.Missing
                : UnixEntry.Unreadable;
        }

        if (!TryFieldOffsets(out int modeAt, out int userAt, out int groupAt))
        {
            return UnixEntry.Unreadable;
        }

        uint mode = BitConverter.ToUInt32(buffer, modeAt);
        var permissions = (UnixFileMode)(mode & PermissionMask);

        if (!ReadsBackTheSamePermissions(path, permissions))
        {
            return UnixEntry.Unreadable;
        }

        return new UnixEntry(
            (mode & FileTypeMask) == SocketFileType ? UnixPathKind.Socket : UnixPathKind.Other,
            permissions,
            BitConverter.ToUInt32(buffer, userAt),
            BitConverter.ToUInt32(buffer, groupAt)
        );
    }

    public static bool TryGiveToGroup(string path, uint groupId, out string problem)
    {
        problem = string.Empty;

        if (Chown(path, LeaveTheOwnerAlone, groupId) is 0)
        {
            return true;
        }

        problem = Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());

        return false;
    }

    public static string Octal(UnixFileMode permissions) =>
        Convert.ToString((int)permissions, 8).PadLeft(4, '0');

    private static bool ReadsBackTheSamePermissions(string path, UnixFileMode permissions)
    {
        try
        {
            return File.GetUnixFileMode(path) == permissions;
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryFieldOffsets(out int modeAt, out int userAt, out int groupAt)
    {
        switch (RuntimeInformation.ProcessArchitecture)
        {
            case Architecture.X64:
                (modeAt, userAt, groupAt) = (24, 28, 32);

                return true;

            case Architecture.Arm64:
                (modeAt, userAt, groupAt) = (16, 24, 28);

                return true;

            default:
                (modeAt, userAt, groupAt) = (0, 0, 0);

                return false;
        }
    }

    [LibraryImport(
        "libc",
        EntryPoint = "stat",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true
    )]
    private static partial int Stat(string path, byte[] buffer);

    [LibraryImport(
        "libc",
        EntryPoint = "chown",
        StringMarshalling = StringMarshalling.Utf8,
        SetLastError = true
    )]
    private static partial int Chown(string path, uint owner, uint group);

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUserId();

    [LibraryImport("libc", EntryPoint = "getgid")]
    private static partial uint GetGroupId();
}
