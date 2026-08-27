using Carina.Api.Controllers.Epg;
using Carina.Infrastructure.Programmes;

using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class SearchVocabularyTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Description.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.AsType() != typeof(SearchProgrammesAction))
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];

        foreach (ProgrammeSearchTerm term in ProgrammeSearchQuery.Vocabulary)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = term.Name,
                In = ParameterLocation.Query,
                Required = false,
                Schema = term.Repeated
                    ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = Spelt(term.Shape) }
                    : Spelt(term.Shape),
            });
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema Spelt(Type shape)
    {
        if (shape.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. Enum.GetNames(shape).Select(name => (System.Text.Json.Nodes.JsonNode)name)],
            };
        }

        if (shape == typeof(int))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" };
        }

        if (shape == typeof(bool))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Boolean };
        }

        return shape == typeof(DateTimeOffset)
            ? new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
            : new OpenApiSchema { Type = JsonSchemaType.String };
    }
}
