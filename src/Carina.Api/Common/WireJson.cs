using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Api.Common;

public static class WireJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
