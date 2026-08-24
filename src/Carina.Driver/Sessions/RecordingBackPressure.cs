namespace Carina.Driver.Sessions;

public static class RecordingBackPressure
{
    public const long FastestBytesPerSecond = 16_500_000 / 8;

    public static TimeSpan WithinTheDemuxWindow(int demuxBufferBytes) =>
        TimeSpan.FromSeconds(Math.Floor(demuxBufferBytes / (double)FastestBytesPerSecond));
}
