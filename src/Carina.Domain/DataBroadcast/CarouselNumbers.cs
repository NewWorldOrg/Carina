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

    public const int MostPathBytes = 0xFFFF;

    internal static int Tag(int value, string name)
        => Within(value, MostTag, name, "A component tag is one byte.");

    internal static int ModuleId(int value, string name)
        => Within(value, MostModuleId, name, "A module id is two bytes.");

    internal static int Version(int value, string name)
        => Within(value, MostVersion, name, "A module version is one byte.");

    internal static string Path(string value, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, name);

        if (System.Text.Encoding.UTF8.GetByteCount(value) > MostPathBytes)
        {
            throw new ArgumentOutOfRangeException(name, value.Length, "A path is told in two bytes of length.");
        }

        return value;
    }

    internal static string MediaType(string value, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, name);

        return value;
    }

    internal static int Within(int value, int most, string name, string message)
    {
        if (value < 0 || value > most)
        {
            throw new ArgumentOutOfRangeException(name, value, message);
        }

        return value;
    }
}
