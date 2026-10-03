using System.Text;

namespace Carina.Broadcast.Text;

/// <summary>
/// One change of the text on a caption screen, a number of tenths of a second after the statement that made
/// it began: the text shown, line by line, or null when nothing is left on the screen.
/// </summary>
public sealed record AribCaptionChange(int AfterTenths, string? Text);

/// <summary>
/// The text of the captions on screen, written one statement after another in the eight-unit code, with the
/// default macros that designate the caption sets followed and any other macro passed over. The screen
/// is cleared only when a statement says so or when a statement ends with a wait; a statement that does
/// neither adds to what is there. Ruby, colours, sizes and positions are left behind, a move to another row
/// starts a new line, and a character only the broadcast's own glyph can show becomes
/// <see cref="Unreplaceable"/>.
/// </summary>
public sealed class AribCaptionScreen
{
    public const char Unreplaceable = '〓';

    private const byte Escape = 0x1B;

    private const byte ActivePositionDown = 0x0A;

    private const byte ClearScreen = 0x0C;

    private const byte ActivePositionReturn = 0x0D;

    private const byte LockingShiftOne = 0x0E;

    private const byte LockingShiftZero = 0x0F;

    private const byte ParameterizedActivePositionForward = 0x16;

    private const byte SingleShiftTwo = 0x19;

    private const byte ActivePositionSet = 0x1C;

    private const byte SingleShiftThree = 0x1D;

    private const byte SmallSize = 0x88;

    private const byte MiddleSize = 0x89;

    private const byte NormalSize = 0x8A;

    private const byte CharacterSize = 0x8B;

    private const byte TinySize = 0x60;

    private const byte ControlSequence = 0x9B;

    private const byte ActiveCoordinatePositionSet = 0x61;

    private const byte Time = 0x9D;

    private const byte WaitFor = 0x20;

    private const byte Delete = 0x7F;

    private const int ParameterBase = 0x40;

    private const int Unplaced = -1;

    private static readonly Dictionary<int, GraphicSet[]> DefaultMacros = new()
    {
        [0x60] = [GraphicSet.Kanji, GraphicSet.Alphanumeric, GraphicSet.Hiragana, GraphicSet.Macro],
        [0x61] = [GraphicSet.Kanji, GraphicSet.Katakana, GraphicSet.Hiragana, GraphicSet.Macro],
        [0x62] = [GraphicSet.Kanji, GraphicSet.OneByteDrcs, GraphicSet.Hiragana, GraphicSet.Macro],
        [0x6B] = [GraphicSet.Kanji, GraphicSet.OneByteDrcs, GraphicSet.Hiragana, GraphicSet.Macro],
        [0x6C] = [GraphicSet.Kanji, GraphicSet.OneByteDrcs, GraphicSet.Hiragana, GraphicSet.Macro],
        [0x6D] = [GraphicSet.Kanji, GraphicSet.OneByteDrcs, GraphicSet.Hiragana, GraphicSet.Macro],
    };

    private readonly StringBuilder written = new();

    private int row = Unplaced;

    private string? shown;

    /// <summary>
    /// Writes the body of one statement onto the screen and says each change of what it shows.
    /// </summary>
    public IReadOnlyList<AribCaptionChange> Write(ReadOnlySpan<byte> statement)
    {
        Statement reading = new(this);

        reading.Read(statement);

        return reading.Changes;
    }

    private static string? Visible(StringBuilder text)
    {
        string[] lines = [.. text.ToString()
            .Split('\n')
            .Select(line => line.Trim(' ', '　'))
            .Where(line => line.Length > 0)];

        return lines.Length is 0 ? null : string.Join('\n', lines);
    }

    private static bool IsPrivate(char character) => character is >= '' and <= '';

    private void Clear()
    {
        written.Clear();
        row = Unplaced;
    }

    private void BreakLine()
    {
        if (written.Length > 0 && written[^1] != '\n')
        {
            written.Append('\n');
        }
    }

    private void GoToRow(int newRow)
    {
        if (newRow != row)
        {
            BreakLine();
        }

        row = newRow;
    }

    private void Down()
    {
        BreakLine();

        if (row is not Unplaced)
        {
            row++;
        }
    }

    private sealed class Statement(AribCaptionScreen screen)
    {
        private readonly GraphicSet[] designated =
        [
            GraphicSet.Kanji,
            GraphicSet.Alphanumeric,
            GraphicSet.Hiragana,
            GraphicSet.Macro,
        ];

        private readonly List<AribCaptionChange> changes = [];

        private int left;

        private int right = 2;

        private int single = -1;

        private bool small;

        private int tenths;

        private bool changed;

        private bool waitedLast;

        public IReadOnlyList<AribCaptionChange> Changes => changes;

        public void Read(ReadOnlySpan<byte> bytes)
        {
            int at = 0;

            while (at < bytes.Length)
            {
                at = Step(bytes, at);
            }

            End();
        }

        private int Step(ReadOnlySpan<byte> bytes, int at)
        {
            byte code = bytes[at];

            if (code == Escape)
            {
                return AribText.ReadEscape(bytes, at + 1, designated, ref left, ref right);
            }

            if (code < 0x20)
            {
                return Control(bytes, at);
            }

            if (code is >= 0x80 and <= 0x9F)
            {
                return Extended(bytes, at);
            }

            if (code is 0x20 or 0xA0)
            {
                single = -1;
                Put(" ");

                return at + 1;
            }

            if (code is Delete or 0xFF)
            {
                single = -1;

                return at + 1;
            }

            return Character(bytes, at);
        }

        private int Control(ReadOnlySpan<byte> bytes, int at)
        {
            switch (bytes[at])
            {
                case ClearScreen:
                    screen.Clear();
                    Touched();

                    return at + 1;
                case ActivePositionReturn or ActivePositionDown:
                    screen.Down();

                    return at + 1;
                case ActivePositionSet:
                    if (at + 2 < bytes.Length)
                    {
                        screen.GoToRow(bytes[at + 1] - ParameterBase);
                    }

                    return at + 3;
                case ParameterizedActivePositionForward:
                    return at + 2;
                case LockingShiftZero:
                    left = 0;

                    return at + 1;
                case LockingShiftOne:
                    left = 1;

                    return at + 1;
                case SingleShiftTwo:
                    single = 2;

                    return at + 1;
                case SingleShiftThree:
                    single = 3;

                    return at + 1;
                default:
                    return at + 1;
            }
        }

        private int Extended(ReadOnlySpan<byte> bytes, int at)
        {
            switch (bytes[at])
            {
                case SmallSize:
                    small = true;

                    return at + 1;
                case MiddleSize or NormalSize:
                    small = false;

                    return at + 1;
                case CharacterSize:
                    small = at + 1 < bytes.Length && bytes[at + 1] == TinySize;

                    return at + 2;
                case Time when at + 2 < bytes.Length && bytes[at + 1] == WaitFor:
                    Wait(bytes[at + 2] - ParameterBase);

                    return at + 3;
                case ControlSequence:
                    return Sequence(bytes, at);
                default:
                    return AribText.ReadC1(bytes, at);
            }
        }

        private int Sequence(ReadOnlySpan<byte> bytes, int at)
        {
            int after = AribText.ReadC1(bytes, at);

            if (after <= bytes.Length && after > at + 1 && bytes[after - 1] == ActiveCoordinatePositionSet)
            {
                screen.BreakLine();
                screen.row = Unplaced;
            }

            return after;
        }

        private int Character(ReadOnlySpan<byte> bytes, int at)
        {
            byte code = bytes[at];
            GraphicSet set = single >= 0 ? designated[single] : designated[code >= 0x80 ? right : left];
            single = -1;
            int width = AribGraphicSets.Width(set);

            if (at + width > bytes.Length)
            {
                return bytes.Length;
            }

            if (width is 2 && !AribText.IsGraphic(bytes[at + 1]))
            {
                Put(AribText.UnknownCharacter.ToString());

                return at + 1;
            }

            if (set is GraphicSet.Macro)
            {
                Invoke(code & 0x7F);

                return at + width;
            }

            Put(Glyph(set, bytes.Slice(at, width)));

            return at + width;
        }

        private void Invoke(int macro)
        {
            if (!DefaultMacros.TryGetValue(macro, out GraphicSet[]? sets))
            {
                return;
            }

            sets.CopyTo(designated, 0);
            left = 0;
            right = 2;
        }

        private static string Glyph(GraphicSet set, ReadOnlySpan<byte> code)
        {
            if (AribGraphicSets.IsDrcs(set))
            {
                return Unreplaceable.ToString();
            }


            StringBuilder glyph = new();
            AribText.Append(glyph, set, code);
            string read = glyph.ToString();

            return read.Length is 1 && IsPrivate(read[0]) ? Unreplaceable.ToString() : read;
        }

        private void Put(string text)
        {
            if (small || text.Length is 0)
            {
                return;
            }

            screen.written.Append(text);
            Touched();
        }

        private void Touched()
        {
            changed = true;
            waitedLast = false;
        }

        private void Wait(int forTenths)
        {
            Say();
            tenths += Math.Max(0, forTenths);
            waitedLast = true;
        }

        private void End()
        {
            if (changed)
            {
                Say();

                return;
            }

            if (waitedLast && screen.shown is not null)
            {
                screen.Clear();
                Say();
            }
        }

        private void Say()
        {
            changed = false;
            string? now = Visible(screen.written);

            if (string.Equals(now, screen.shown, StringComparison.Ordinal))
            {
                return;
            }

            screen.shown = now;
            changes.Add(new AribCaptionChange(tenths, now));
        }
    }
}
