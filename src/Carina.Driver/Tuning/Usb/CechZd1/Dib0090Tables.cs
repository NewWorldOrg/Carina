namespace Carina.Driver.Tuning.Usb.CechZd1;

public sealed record TunerBandRow(
    int LimitKilohertz,
    int Switch,
    int LnaTune,
    int LnaBias,
    int VoltageToCurrent,
    int Mixer,
    int Load,
    int Enable
);

public sealed record SynthesiserRow(
    int LimitKilohertz,
    int LimitKilohertzOnP1g,
    int VcoBand,
    int DividerSign,
    int DividerSignOnP1g,
    int Divider,
    int Prescaler
);

public sealed record WidebandDetectorRow(
    int LimitMegahertz,
    int ColdSlope,
    int ColdBase,
    int HotSlope,
    int HotBase,
    int Gain
);

public sealed record GainRamp(IReadOnlyList<int> Values);

public static class Dib0090Tables
{
    public const int CBandEnable = 0x88E9;

    public const int VhfEnable = 0x44E9;

    public const int UhfEnable = 0x22E9;

    public static readonly IReadOnlyList<ushort> Defaults =
    [
        0x0000, 0x99A0, 0x6008, 0x0000, 0x8BCB, 0x0000, 0x0405, 0x0000, 0x0000, 0x0000, 0xB802, 0x0300, 0x2D12,
        0xBAC0, 0x7C00, 0xDBB9, 0x0954, 0x0743, 0x8000, 0x0001, 0x0040, 0x0100, 0x0000, 0xE910, 0x149E,
    ];

    public static readonly IReadOnlyList<TunerBandRow> Bands =
    [
        new(170000, 4, 1, 15, 0x280, 0x2912, 0xB94E, CBandEnable),
        new(184000, 1, 1, 15, 0x300, 0x4D12, 0xB94E, VhfEnable),
        new(227000, 1, 3, 15, 0x300, 0x4D12, 0xB94E, VhfEnable),
        new(380000, 1, 7, 15, 0x300, 0x4D12, 0xB94E, VhfEnable),
        new(520000, 2, 0, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(550000, 2, 2, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(650000, 2, 3, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(750000, 2, 5, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(850000, 2, 6, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(900000, 2, 7, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
    ];

    public static readonly IReadOnlyList<TunerBandRow> BandsOnP1g =
    [
        new(170000, 4, 1, 0x820F, 0x300, 0x2D22, 0x82CB, CBandEnable),
        new(184000, 1, 1, 15, 0x300, 0x4D12, 0xB94E, VhfEnable),
        new(227000, 1, 3, 15, 0x300, 0x4D12, 0xB94E, VhfEnable),
        new(380000, 1, 7, 15, 0x300, 0x4D12, 0xB94E, VhfEnable),
        new(510000, 2, 0, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(540000, 2, 1, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(600000, 2, 3, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(630000, 2, 4, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(680000, 2, 5, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(720000, 2, 6, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
        new(900000, 2, 7, 15, 0x300, 0x1D12, 0xB9CE, UhfEnable),
    ];

    public static readonly IReadOnlyList<SynthesiserRow> Synthesiser =
    [
        new(56000, 57000, 0, 9, 11, 48, 6),
        new(70000, 70000, 1, 9, 11, 48, 6),
        new(87000, 86000, 0, 8, 10, 32, 4),
        new(105000, 105000, 1, 8, 10, 32, 4),
        new(115000, 115000, 0, 7, 9, 24, 6),
        new(140000, 140000, 1, 7, 9, 24, 6),
        new(170000, 170000, 0, 6, 8, 16, 4),
        new(200000, 200000, 1, 6, 8, 16, 4),
        new(230000, 230000, 0, 5, 7, 12, 6),
        new(280000, 280000, 1, 5, 7, 12, 6),
        new(340000, 340000, 0, 4, 6, 8, 4),
        new(380000, 380000, 1, 4, 6, 8, 4),
        new(450000, 455000, 0, 3, 5, 6, 6),
        new(580000, 580000, 1, 3, 5, 6, 6),
        new(700000, 680000, 0, 2, 4, 4, 4),
        new(860000, 860000, 1, 2, 4, 4, 4),
    ];

    public static readonly IReadOnlyList<WidebandDetectorRow> WidebandDetector =
    [
        new(120, 0, 500, 0, 500, 4),
        new(170, 0, 450, 0, 450, 4),
        new(380, 48, 373, 28, 259, 6),
        new(860, 34, 700, 36, 616, 6),
        new(65535, 34, 700, 36, 616, 6),
    ];

    public static readonly GainRamp RadioFrequencyVhfUhf = new(
        [398, 24, 954, 0x1C00, 0x0122, 0x42BB, 0x03BA, 0x4644, 0x02BB, 0x1D22, 0x0244]
    );

    public static readonly GainRamp Baseband = new([500, 8, 400, 0x0400, 0x00A8, 0x04A8, 0x0190]);

    public static TunerBandRow BandFor(int kilohertz, bool p1g) =>
        (p1g ? BandsOnP1g : Bands).First(row => kilohertz <= row.LimitKilohertz);

    public static SynthesiserRow SynthesiserFor(int kilohertz, bool p1g) =>
        Synthesiser.First(row => kilohertz <= (p1g ? row.LimitKilohertzOnP1g : row.LimitKilohertz));

    public static WidebandDetectorRow DetectorFor(int megahertz) =>
        WidebandDetector.First(row => megahertz <= row.LimitMegahertz);
}
