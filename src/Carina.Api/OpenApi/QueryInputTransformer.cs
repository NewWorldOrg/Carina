using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class QueryInputTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        QueryInput[] declared = [.. context.Description.ActionDescriptor.EndpointMetadata.OfType<QueryInput>()];

        if (declared.Length is 0)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];

        foreach (QueryInput input in declared)
        {
            operation.Parameters.Add(input.Parameter());
        }

        return Task.CompletedTask;
    }
}
