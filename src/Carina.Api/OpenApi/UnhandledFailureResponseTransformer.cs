using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class UnhandledFailureResponseTransformer : IOpenApiOperationTransformer
{
    private const string Failure = "500";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        operation.Responses ??= [];
        operation.Responses[Failure] = new OpenApiResponse
        {
            Description = "The request failed before it could answer for itself. The body carries the usual envelope with no data.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { "status", "message", "data" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                            ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["data"] = new OpenApiSchema { Type = JsonSchemaType.Null },
                        },
                    },
                },
            },
        };

        return Task.CompletedTask;
    }
}
