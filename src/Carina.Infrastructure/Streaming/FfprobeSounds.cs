using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class FfprobeSounds
{
    private const string Programmes = "programs";
    private const string ProgrammeNumber = "program_id";
    private const string Streams = "streams";
    private const string CodecType = "codec_type";
    private const string Audio = "audio";

    public static CarriedSounds Read(string output, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(service);

        JsonElement said;

        try
        {
            said = JsonDocument.Parse(output).RootElement.Clone();
        }
        catch (JsonException broken)
        {
            return CarriedSounds.Unread($"the programme answered with something that is not JSON: {broken.Message}");
        }

        if (said.ValueKind is not JsonValueKind.Object
            || !said.TryGetProperty(Programmes, out JsonElement programmes)
            || programmes.ValueKind is not JsonValueKind.Array)
        {
            return CarriedSounds.Unread("the programme named no programmes in the stream");
        }

        foreach (JsonElement programme in programmes.EnumerateArray())
        {
            if (NumberOf(programme) == service.Value)
            {
                return CarriedSounds.Counted(SoundsIn(programme));
            }
        }

        return CarriedSounds.Unread("the stream carries no programme of the service this recording was made from");
    }

    private static int? NumberOf(JsonElement programme)
        => programme.ValueKind is JsonValueKind.Object
           && programme.TryGetProperty(ProgrammeNumber, out JsonElement number)
           && number.ValueKind is JsonValueKind.Number
           && number.TryGetInt32(out int said)
            ? said
            : null;

    private static int SoundsIn(JsonElement programme)
        => programme.TryGetProperty(Streams, out JsonElement streams) && streams.ValueKind is JsonValueKind.Array
            ? streams.EnumerateArray().Count(IsSound)
            : 0;

    private static bool IsSound(JsonElement stream)
        => stream.ValueKind is JsonValueKind.Object
           && stream.TryGetProperty(CodecType, out JsonElement kind)
           && kind.ValueKind is JsonValueKind.String
           && string.Equals(kind.GetString(), Audio, StringComparison.Ordinal);
}
