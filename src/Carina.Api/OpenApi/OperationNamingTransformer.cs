using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class OperationNamingTransformer : IOpenApiOperationTransformer
{
    private const string ActionSuffix = "Action";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Description.ActionDescriptor is not ControllerActionDescriptor action)
        {
            return Task.CompletedTask;
        }

        operation.OperationId = OperationId(action);
        operation.Tags = new HashSet<OpenApiTagReference>
        {
            new(Tag(context.Description), context.Document),
        };

        return Task.CompletedTask;
    }

    public static string OperationId(ControllerActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);

        string name = action.ControllerName;
        if (name.EndsWith(ActionSuffix, StringComparison.Ordinal))
        {
            name = name[..^ActionSuffix.Length];
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static string Tag(ApiDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        string[] segments = (description.RelativePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length > 1 && segments[0] == "api")
        {
            return segments[1];
        }

        return segments.Length > 0 ? segments[0] : "api";
    }
}
