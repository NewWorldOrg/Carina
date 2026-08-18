using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Api.Common;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = Built();

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private static JsonSerializerOptions Built()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Configure(options);

        return options;
    }
}
