using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Infrastructure.Persistence.Configurations;

internal static class ProgrammeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
