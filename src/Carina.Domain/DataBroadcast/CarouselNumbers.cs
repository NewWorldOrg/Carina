namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The ranges the numbers of a data carousel are carried in.
/// </summary>
public static class CarouselNumbers
{
    public const int MostTag = 0xFF;

    public const int MostModuleId = 0xFFFF;

    public const int MostVersion = 0xFF;

    public const int MostEventGroup = 0xFFF;

    public const int MostEventId = 0xFFFF;

    public const int MostEventType = 0xFF;

    internal static int Tag(int value, string name)
        => Within(value, MostTag, name, "A component tag is one byte.");

    internal static int ModuleId(int value, string name)
        => Within(value, MostModuleId, name, "A module id is two bytes.");

    internal static int Version(int value, string name)
        => Within(value, MostVersion, name, "A module version is one byte.");

    internal static int Within(int value, int most, string name, string message)
    {
        if (value < 0 || value > most)
        {
            throw new ArgumentOutOfRangeException(name, value, message);
        }

        return value;
    }
}
