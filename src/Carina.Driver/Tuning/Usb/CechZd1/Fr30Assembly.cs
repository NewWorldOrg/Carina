namespace Carina.Driver.Tuning.Usb.CechZd1;

public enum Fr30Condition
{
    Always = 0x0,

    Equal = 0x2,

    NotEqual = 0x3,

    Carry = 0x4,

    NoCarry = 0x5,

    Negative = 0x6,

    LowerOrSame = 0xE,
}

/// <summary>
/// Writes FR30 instructions into words, two big endian instructions to a word, as the bridge's processors read them.
/// A branch whose target is out of reach becomes the inverse branch over a load of the target and a jump.
/// </summary>
public sealed class Fr30Assembly
{
    public const ushort Nop = 0x9FA0;

    private const int ShortReach = 128;
    private const int FarJumpHalves = 3;

    private readonly List<Item> items = [];
    private readonly int scratch;

    /// <param name="scratch">The register a far branch may load its target into.</param>
    public Fr30Assembly(int scratch)
    {
        this.scratch = Register(scratch);
    }

    public Fr30Assembly Label(string name)
    {
        items.Add(new Item(Kind.Label, [], name, Fr30Condition.Always));

        return this;
    }

    public Fr30Assembly Load(uint value, int register) =>
        value switch
        {
            <= 0xFF => Halves((ushort)(0xC000 + (value * 0x10) + Register(register))),
            <= 0xFFFFF => LoadTwenty(value, register),
            _ => Halves((ushort)(0x9F80 + Register(register)), (ushort)(value >> 16), (ushort)value),
        };

    public Fr30Assembly LoadTwenty(uint value, int register) =>
        value > 0xFFFFF
            ? throw new ArgumentOutOfRangeException(nameof(value), value, "LDI:20 carries 20 bits.")
            : Halves((ushort)(0x9B00 + (((value >> 16) & 0xF) * 0x10) + Register(register)), (ushort)value);

    public Fr30Assembly LoadWord(int from, int into) => Pair(0x0400, from, into);

    public Fr30Assembly LoadHalf(int from, int into) => Pair(0x0500, from, into);

    public Fr30Assembly LoadByte(int from, int into) => Pair(0x0600, from, into);

    public Fr30Assembly StoreWord(int value, int at) => Pair(0x1400, at, value);

    public Fr30Assembly StoreHalf(int value, int at) => Pair(0x1500, at, value);

    public Fr30Assembly StoreByte(int value, int at) => Pair(0x1600, at, value);

    public Fr30Assembly Move(int from, int into) => Pair(0x8B00, from, into);

    public Fr30Assembly Add(int from, int into) => Pair(0xA600, from, into);

    public Fr30Assembly AddSmall(uint small, int into) => Small(0xA400, small, into);

    public Fr30Assembly Subtract(int from, int into) => Pair(0xAC00, from, into);

    public Fr30Assembly SubtractSmall(int amount, int into) =>
        amount is < 1 or > 16
            ? throw new ArgumentOutOfRangeException(nameof(amount), amount, "ADD2 takes -16 to -1.")
            : Halves((ushort)(0xA500 + (((uint)-amount & 0xF) * 0x10) + Register(into)));

    public Fr30Assembly Compare(int with, int register) => Pair(0xAA00, with, register);

    public Fr30Assembly CompareSmall(uint small, int register) => Small(0xA800, small, register);

    public Fr30Assembly And(int from, int into) => Pair(0x8200, from, into);

    public Fr30Assembly Or(int from, int into) => Pair(0x9200, from, into);

    public Fr30Assembly ShiftLeft(int count, int register) =>
        count switch
        {
            >= 0 and < 16 => Halves((ushort)(0xB400 + (count * 0x10) + Register(register))),
            >= 16 and < 32 => Halves((ushort)(0xB500 + ((count - 16) * 0x10) + Register(register))),
            _ => throw new ArgumentOutOfRangeException(nameof(count), count, "A shift moves 0 to 31 places."),
        };

    public Fr30Assembly Jump(int register) => Halves((ushort)(0x9700 + Register(register)));

    public Fr30Assembly NoOperation() => Halves(Nop);

    public Fr30Assembly Branch(Fr30Condition condition, string label)
    {
        items.Add(new Item(Kind.Branch, [], label, condition));

        return this;
    }

    /// <summary>
    /// Leaves for an address outside the program through the scratch register.
    /// </summary>
    public Fr30Assembly JumpTo(uint address) => LoadTwenty(address, scratch).Jump(scratch);

    public Fr30Assembly AlignToWord()
    {
        items.Add(new Item(Kind.Align, [], string.Empty, Fr30Condition.Always));

        return this;
    }

    public Fr30Program Assemble(uint origin)
    {
        var far = new HashSet<int>();

        while (true)
        {
            Layout layout = Lay(origin, far);
            int before = far.Count;

            for (int index = 0; index < items.Count; index++)
            {
                if (items[index].Kind is Kind.Branch && !far.Contains(index) && !InReach(layout, index))
                {
                    far.Add(index);
                }
            }

            if (far.Count == before)
            {
                return Emit(origin, layout, far);
            }
        }
    }

    private Fr30Assembly Pair(ushort operation, int source, int target) =>
        Halves((ushort)(operation + (Register(source) * 0x10) + Register(target)));

    private Fr30Assembly Small(ushort operation, uint value, int register) =>
        value > 0xF
            ? throw new ArgumentOutOfRangeException(nameof(value), value, "The short form carries 4 bits.")
            : Halves((ushort)(operation + (value * 0x10) + Register(register)));

    private Fr30Assembly Halves(params ushort[] halves)
    {
        items.Add(new Item(Kind.Halves, halves, string.Empty, Fr30Condition.Always));

        return this;
    }

    private static int Register(int register) =>
        register is >= 0 and < 16
            ? register
            : throw new ArgumentOutOfRangeException(nameof(register), register, "FR30 has R0 to R15.");

    private bool InReach(Layout layout, int index)
    {
        int displacement = Displacement(layout, index, items[index].Text);

        return displacement is >= -ShortReach and < ShortReach;
    }

    private static int Displacement(Layout layout, int index, string label) =>
        layout.Labels.TryGetValue(label, out uint target)
            ? ((int)target - ((int)layout.Starts[index] + 2)) / 2
            : throw new InvalidOperationException($"The program branches to '{label}', which it never defines.");

    private Layout Lay(uint origin, HashSet<int> far)
    {
        var labels = new Dictionary<string, uint>(StringComparer.Ordinal);
        uint[] starts = new uint[items.Count];
        uint at = origin;

        for (int index = 0; index < items.Count; index++)
        {
            starts[index] = at;
            at += (uint)(2 * HalvesOf(index, at, far));

            if (items[index].Kind is Kind.Label)
            {
                labels[items[index].Text] = at;
            }
        }

        return new Layout(labels, starts);
    }

    private int HalvesOf(int index, uint at, HashSet<int> far) =>
        items[index].Kind switch
        {
            Kind.Halves => items[index].Halves.Length,
            Kind.Branch => FarHalves(index, far),
            Kind.Align => at % 4 is 0 ? 0 : 1,
            _ => 0,
        };

    private int FarHalves(int index, HashSet<int> far)
    {
        if (!far.Contains(index))
        {
            return 1;
        }

        return items[index].Condition is Fr30Condition.Always ? FarJumpHalves : FarJumpHalves + 1;
    }

    private Fr30Program Emit(uint origin, Layout layout, HashSet<int> far)
    {
        var halves = new List<ushort>();

        for (int index = 0; index < items.Count; index++)
        {
            Item item = items[index];

            switch (item.Kind)
            {
                case Kind.Halves:
                    halves.AddRange(item.Halves);
                    break;
                case Kind.Align when layout.Starts[index] % 4 is not 0:
                    halves.Add(Nop);
                    break;
                case Kind.Branch when far.Contains(index):
                    halves.AddRange(FarBranch(item, layout.Labels[item.Text]));
                    break;
                case Kind.Branch:
                    halves.Add(ShortBranch(item.Condition, Displacement(layout, index, item.Text)));
                    break;
            }
        }

        if (halves.Count % 2 is not 0)
        {
            halves.Add(Nop);
        }

        uint[] words = new uint[halves.Count / 2];

        for (int word = 0; word < words.Length; word++)
        {
            words[word] = ((uint)halves[2 * word] << 16) | halves[(2 * word) + 1];
        }

        return new Fr30Program(origin, words, layout.Labels);
    }

    private IEnumerable<ushort> FarBranch(Item item, uint target)
    {
        if (item.Condition is not Fr30Condition.Always)
        {
            yield return ShortBranch(Inverse(item.Condition), FarJumpHalves);
        }

        yield return (ushort)(0x9B00 + (((target >> 16) & 0xF) * 0x10) + scratch);
        yield return (ushort)target;
        yield return (ushort)(0x9700 + scratch);
    }

    private static ushort ShortBranch(Fr30Condition condition, int displacement) =>
        (ushort)(0xE000 + ((int)condition * 0x100) + (displacement & 0xFF));

    private static Fr30Condition Inverse(Fr30Condition condition) =>
        condition switch
        {
            Fr30Condition.Equal => Fr30Condition.NotEqual,
            Fr30Condition.NotEqual => Fr30Condition.Equal,
            Fr30Condition.Carry => Fr30Condition.NoCarry,
            Fr30Condition.NoCarry => Fr30Condition.Carry,
            _ => throw new InvalidOperationException(
                $"A branch on {condition} is out of reach, and it has no inverse to step over a far jump with."
            ),
        };

    private enum Kind
    {
        Halves,
        Label,
        Branch,
        Align,
    }

    private sealed record Item(Kind Kind, ushort[] Halves, string Text, Fr30Condition Condition);

    private sealed record Layout(Dictionary<string, uint> Labels, uint[] Starts);
}

public sealed record Fr30Program(uint Origin, IReadOnlyList<uint> Words, IReadOnlyDictionary<string, uint> Labels)
{
    public uint AddressOf(string label) => Labels[label];
}
