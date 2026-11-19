using System.Text.Json.Nodes;

using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder.Notifications;
using Carina.Domain.Notifications;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class RuleKindsSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string Events = "events";

    private static readonly HashSet<Type> SetsOfRules =
    [
        typeof(CreateNotificationRuleRequest),
        typeof(ReviseNotificationRuleRequest),
        typeof(NotificationRuleResponder),
    ];

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (context.JsonPropertyInfo is not { Name: Events } property || !SetsOfRules.Contains(property.DeclaringType))
        {
            return Task.CompletedTask;
        }

        schema.Items = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [.. NoticeKinds.ChosenByRules.Select(kind => (JsonNode)JsonValue.Create(WireNameText.Spelled(kind)))],
        };

        return Task.CompletedTask;
    }
}
