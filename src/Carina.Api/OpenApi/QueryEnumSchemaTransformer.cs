using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class QueryEnumSchemaTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (operation.Parameters is null || context.Document is not { } document)
        {
            return;
        }

        foreach (ApiParameterDescription taken in context.Description.ParameterDescriptions)
        {
            if (!TakesAnEnumFromTheQuery(taken, out Type choice)
                || operation.Parameters.OfType<OpenApiParameter>().FirstOrDefault(parameter => Describes(parameter, taken)) is not { } parameter)
            {
                continue;
            }

            if (document.Components?.Schemas?.ContainsKey(choice.Name) is not true)
            {
                document.AddComponent(choice.Name, await SchemaOfAsync(taken.Type, context, cancellationToken));
            }

            parameter.Schema = new OpenApiSchemaReference(choice.Name, document);
        }
    }

    private static async Task<OpenApiSchema> SchemaOfAsync(
        Type taken,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        OpenApiSchema schema = await context.GetOrCreateSchemaAsync(taken, null, cancellationToken);

        if (Nullable.GetUnderlyingType(taken) is not null && schema.Enum is { } values && !values.Any(value => value is null))
        {
            values.Add(null!);
            schema.Type |= JsonSchemaType.Null;
        }

        return schema;
    }

    private static bool TakesAnEnumFromTheQuery(ApiParameterDescription taken, out Type choice)
    {
        choice = Nullable.GetUnderlyingType(taken.Type) ?? taken.Type;

        return taken.Source == BindingSource.Query && choice.IsEnum;
    }

    private static bool Describes(OpenApiParameter parameter, ApiParameterDescription taken)
        => parameter.In == ParameterLocation.Query
            && string.Equals(parameter.Name, taken.Name, StringComparison.Ordinal);
}
