using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public sealed record ThumbnailRequest
{
    public ThumbnailRequest(string source, string destination, ServiceId service, TimeSpan at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(service);

        if (at < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(at), at, "A picture is taken out of a recording, not before it.");
        }

        Source = source;
        Destination = destination;
        Service = service;
        At = at;
    }

    public string Source { get; }

    public string Destination { get; }

    public ServiceId Service { get; }

    public TimeSpan At { get; }
}

public sealed record ThumbnailFrameRequest
{
    public ThumbnailFrameRequest(string source, ServiceId service, TimeSpan at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(service);

        if (at < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(at), at, "A picture is taken out of a recording, not before it.");
        }

        Source = source;
        Service = service;
        At = at;
    }

    public string Source { get; }

    public ServiceId Service { get; }

    public TimeSpan At { get; }
}

public sealed record ThumbnailRender
{
    public const int LongestNote = 500;

    private ThumbnailRender(ThumbnailFault? fault, int? exitCode, string note, byte[]? picture)
    {
        Fault = fault;
        ExitCode = exitCode;
        Note = note;
        Picture = picture;
    }

    public ThumbnailFault? Fault { get; }

    public int? ExitCode { get; }

    public string Note { get; }

    public byte[]? Picture { get; }

    public bool Drew => Fault is null;

    public static ThumbnailRender Drawn() => new(null, null, string.Empty, null);

    public static ThumbnailRender Drawn(byte[] picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        return picture.Length > 0
            ? new ThumbnailRender(null, null, string.Empty, picture)
            : throw new ArgumentException("A picture that weighs nothing was not drawn.", nameof(picture));
    }

    public static ThumbnailRender Refused(int exitCode, string note)
    {
        if (exitCode is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                exitCode,
                "A programme that exited 0 was not refused by it.");
        }

        return new ThumbnailRender(ThumbnailFault.Refused, exitCode, Shortened(note), null);
    }

    public static ThumbnailRender Failed(ThumbnailFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "A thumbnail fault is one the ledger holds.");
        }

        if (fault is ThumbnailFault.Refused)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                $"A programme that refused says with what code, so {nameof(Refused)} takes one.");
        }

        return new ThumbnailRender(fault, null, Shortened(note), null);
    }

    private static string Shortened(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        string trimmed = note.Trim();

        return trimmed.Length <= LongestNote ? trimmed : trimmed[^LongestNote..];
    }
}
