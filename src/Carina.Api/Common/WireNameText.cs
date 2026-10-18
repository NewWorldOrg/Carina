using System.Text.Json;

namespace Carina.Api.Common;

/// <summary>
/// Reads a value of an enumeration by the name it goes by on the wire, its name in camel case, and by no other
/// spelling or number.
/// </summary>
public static class WireNameText
{
    public static T? Read<T>(string? text)
        where T : struct, Enum
        => Enum.GetValues<T>().Where(value => string.Equals(Spelled(value), text, StringComparison.Ordinal)).Select(value => (T?)value).FirstOrDefault();

    public static string Spelled<T>(T value)
        where T : struct, Enum
        => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
