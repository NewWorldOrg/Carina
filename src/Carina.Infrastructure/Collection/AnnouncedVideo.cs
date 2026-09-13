using Carina.Broadcast.Descriptors;
using Carina.Domain.Base;

namespace Carina.Infrastructure.Collection;

public static class AnnouncedVideo
{
    private const int MpegPicture = 1;

    private const int AvcPicture = 5;

    public static VideoMode ModeOf(IReadOnlyList<ComponentDescription> announced)
    {
        ArgumentNullException.ThrowIfNull(announced);

        return Mode(First(announced)?.ComponentType);
    }

    public static AspectRatio AspectOf(IReadOnlyList<ComponentDescription> announced)
    {
        ArgumentNullException.ThrowIfNull(announced);

        return Aspect(First(announced)?.ComponentType);
    }

    private static ComponentDescription? First(IReadOnlyList<ComponentDescription> announced)
    {
        foreach (ComponentDescription component in announced)
        {
            if (component.StreamContent is MpegPicture or AvcPicture)
            {
                return component;
            }
        }

        return null;
    }

    private static VideoMode Mode(int? componentType)
        => componentType switch
        {
            >= 0x01 and <= 0x04 => VideoMode.Interlaced480,
            0x83 => VideoMode.Progressive4320,
            >= 0x91 and <= 0x94 => VideoMode.Progressive2160,
            >= 0xA1 and <= 0xA4 => VideoMode.Progressive480,
            >= 0xB1 and <= 0xB4 => VideoMode.Interlaced1080,
            >= 0xC1 and <= 0xC4 => VideoMode.Progressive720,
            >= 0xD1 and <= 0xD4 => VideoMode.Progressive240,
            >= 0xE1 and <= 0xE4 => VideoMode.Progressive1080,
            >= 0xF1 and <= 0xF4 => VideoMode.Progressive180,
            _ => VideoMode.Undetermined,
        };

    private static AspectRatio Aspect(int? componentType)
    {
        if (Mode(componentType) is VideoMode.Undetermined)
        {
            return AspectRatio.Undetermined;
        }

        return (componentType & 0x0F) switch
        {
            1 => AspectRatio.FourByThree,
            2 => AspectRatio.SixteenByNineWithPanVector,
            3 => AspectRatio.SixteenByNine,
            4 => AspectRatio.WiderThanSixteenByNine,
            _ => AspectRatio.Undetermined,
        };
    }
}
