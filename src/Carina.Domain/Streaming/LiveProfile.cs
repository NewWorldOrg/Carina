namespace Carina.Domain.Streaming;

public sealed class LiveProfile
{
    private static readonly FrameRate EveryFrame = FrameRate.BroadcastFrames;

    private static readonly FrameRate EveryField = FrameRate.Of(60000, 1001);

    private static readonly CardRateControl AlikeInEveryFormat =
        new(new ConstantQuantiser(24), new ConstantQuantiser(24), new QualityIndex(80));

    public static readonly LiveProfile FullHd60 =
        new("1080p60", new VideoSize(1920, 1080), EveryField, new BitrateCap(9000), AlikeInEveryFormat);

    public static readonly LiveProfile FullHd30 =
        new("1080p30", new VideoSize(1920, 1080), EveryFrame, new BitrateCap(6000), AlikeInEveryFormat);

    public static readonly LiveProfile Hd60 =
        new("720p60", new VideoSize(1280, 720), EveryField, new BitrateCap(4500), AlikeInEveryFormat);

    public static readonly LiveProfile Hd30 =
        new("720p30", new VideoSize(1280, 720), EveryFrame, new BitrateCap(3000), AlikeInEveryFormat);

    public static readonly IReadOnlyList<LiveProfile> All = [FullHd60, FullHd30, Hd60, Hd30];

    private LiveProfile(
        string name,
        VideoSize size,
        FrameRate rate,
        BitrateCap softwareRateControl,
        CardRateControl vaapiRateControl)
    {
        Name = name;
        Size = size;
        Rate = rate;
        SoftwareRateControl = softwareRateControl;
        VaapiRateControl = vaapiRateControl;
    }

    public string Name { get; }

    public VideoSize Size { get; }

    public FrameRate Rate { get; }

    public BitrateCap SoftwareRateControl { get; }

    public CardRateControl VaapiRateControl { get; }

    public static LiveProfile Unasked(LiveEncoder encoder)
    {
        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by one of the two the benchmark compared.");
        }

        return encoder is LiveEncoder.Vaapi ? FullHd60 : Hd30;
    }

    public static LiveProfile? Find(string? name)
        => All.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.Ordinal));

    public override string ToString() => Name;
}
