using System.Text.Json.Nodes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class StringEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Enum is null || schema.Enum.Count == 0)
        {
            return Task.CompletedTask;
        }

        bool absent = schema.Enum.Any(value => value is null);

        if (schema.Enum.Where(value => value is not null)
            .All(value => value is JsonValue text && text.TryGetValue<string>(out _)))
        {
            schema.Type = absent ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        }

        return Task.CompletedTask;
    }
}
