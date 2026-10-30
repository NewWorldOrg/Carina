namespace Carina.Driver.Tuning.Usb.CechZd1;

public enum FftSize
{
    Auto = 0,

    TwoK = 1,

    FourK = 2,

    EightK = 3,
}

public enum LayerModulation
{
    Auto = 0,

    Dqpsk = 1,

    Qpsk = 2,

    Qam16 = 3,

    Qam64 = 4,
}

/// <param name="CodeRate">The numerator of 1/2, 2/3, 3/4, 5/6 or 7/8; 0 when it is still to be found.</param>
public sealed record LayerParameters(LayerModulation Modulation, int CodeRate, int Interleave, int Segments)
{
    public static readonly LayerParameters Unknown = new(LayerModulation.Auto, 0, 0, 0);
}

/// <param name="Guard">0 to 3 for 1/32, 1/16, 1/8 and 1/4; -1 when it is still to be found.</param>
public sealed record ChannelParameters(
    FftSize Fft,
    int Guard,
    int Inversion,
    int PartialReception,
    IReadOnlyList<LayerParameters> Layers
)
{
    public const int GuardAuto = -1;

    public const int GuardEighth = 2;

    public static readonly ChannelParameters Unknown = new(
        FftSize.Auto,
        GuardAuto,
        0,
        0,
        [LayerParameters.Unknown, LayerParameters.Unknown, LayerParameters.Unknown]
    );

    public int Mode =>
        Fft switch
        {
            FftSize.TwoK => 1,
            FftSize.FourK => 2,
            _ => 3,
        };

    public ChannelParameters WithLayerA(LayerParameters layer) => this with { Layers = [layer, Layers[1], Layers[2]] };
}
