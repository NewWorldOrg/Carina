using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class DefaultDenyResponseTransformer : IOpenApiOperationTransformer
{
    private const string Refusal = "401";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var anonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();

        if (anonymous)
        {
            return Task.CompletedTask;
        }

        operation.Responses ??= [];
        operation.Responses[Refusal] = new OpenApiResponse
        {
            Description = "Unauthenticated. The default-deny middleware answers with an empty body.",
        };

        return Task.CompletedTask;
    }
}
