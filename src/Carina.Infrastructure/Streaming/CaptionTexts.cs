using Carina.Broadcast.Text;
using Carina.Domain.Captions;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// The text of the captions, read statement by statement from the caption stream carried beside the
/// pictures, as each change on the file's own 90 kHz clock once the lift the moments carry is taken off.
/// A statement that arrives before a change the last one had waited for cuts that change short: from the
/// moment it arrives the screen shows what the last statement left once it ran to its end, and then what
/// this one writes.
/// </summary>
public sealed class CaptionTexts(long lift)
{
    public const long TicksATenth = CaptionCue.Hertz / 10;

    private readonly AribCaptionScreen screen = new();

    private readonly List<CaptionLine> lines = [];

    public IReadOnlyList<CaptionLine> Lines => lines;

    public void Read(NutFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (AribCaptionData.Statement(frame.Data.Span) is not { } body)
        {
            return;
        }

        string? before = screen.Shown;
        IReadOnlyList<AribCaptionChange> changes = screen.Write(body);
        long at = (long)frame.Pts.Value - lift;

        if (lines.RemoveAll(line => line.Pts >= at) > 0)
        {
            Keep(new CaptionLine(at, before));
        }

        foreach (AribCaptionChange change in changes)
        {
            Keep(new CaptionLine(at + (change.AfterTenths * TicksATenth), change.Text));
        }
    }

    private void Keep(CaptionLine line)
    {
        if (lines.Count > 0 && lines[^1].Pts == line.Pts)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > 0 && string.Equals(lines[^1].Text, line.Text, StringComparison.Ordinal))
        {
            return;
        }

        lines.Add(line);
    }
}
