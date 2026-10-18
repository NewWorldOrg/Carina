using System.Text.Json.Nodes;

using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Domain.Segments;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

/// <summary>
/// Lists the values a request property read as text takes, so that a value outside them is refused by what is
/// wrong with it rather than as unreadable input, and the generated client still knows the choices. The values
/// come from where the reading takes them, spelled as the wire spells them; the property may be left out.
/// </summary>
public sealed class WireChoicesSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly Dictionary<(Type Declaring, string Property), IReadOnlyList<string>> Choices = new()
    {
        [(typeof(AnsweredSegmentRequest), "kind")] = [.. AnsweredStretches.KindsTaken.Select(WireNameText.Spelled)],
        [(typeof(AddSkipEventRequest), "event")] = [.. Enum.GetValues<ViewerActionKind>().Select(WireNameText.Spelled)],
        [(typeof(AddSkipEventRequest), "kind")] = [.. SkipEvent.KindsOffered.Select(WireNameText.Spelled)],
    };

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (context.JsonPropertyInfo is not { } property
            || !Choices.TryGetValue((property.DeclaringType, property.Name), out IReadOnlyList<string>? values))
        {
            return Task.CompletedTask;
        }

        schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
        schema.Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value)), null!];

        return Task.CompletedTask;
    }
}
