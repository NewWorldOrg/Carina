using Carina.Broadcast.Text;
using Carina.Domain.Captions;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// The text of the captions, read statement by statement from the caption stream carried beside the
/// pictures, as each change on the file's own 90 kHz clock once the lift the moments carry is taken off.
/// A statement that arrives before a change the last one had waited for replaces that change.
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

        IReadOnlyList<AribCaptionChange> changes = screen.Write(body);

        if (changes.Count is 0)
        {
            return;
        }

        long at = (long)frame.Pts.Value - lift;

        lines.RemoveAll(line => line.Pts >= at);

        foreach (AribCaptionChange change in changes)
        {
            Keep(new CaptionLine(at + (change.AfterTenths * TicksATenth), change.Text));
        }
    }

    private void Keep(CaptionLine line)
    {
        if (lines.Count > 0 && string.Equals(lines[^1].Text, line.Text, StringComparison.Ordinal))
        {
            return;
        }

        lines.Add(line);
    }
}
