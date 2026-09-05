namespace Carina.Infrastructure.Streaming;

public static class FfmpegComplaints
{
    public const string NoCaptionStream = "matches no streams";

    public static bool RefusedForWantOfACaptionStream(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return note.Contains(NoCaptionStream, StringComparison.Ordinal);
    }
}
