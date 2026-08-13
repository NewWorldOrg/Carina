using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string Description =
        "The HTTP surface of the app process. Three contract surfaces do not fit a "
        + "request/response schema and are absent from this document: the transport stream "
        + "(`/sessions/{id}/stream` on the driver socket), the event hub (`GET /api/events`) "
        + "and the bulk programme guide (`GET /api/programs/bulk`). They are declared in "
        + "openapi/non-rest-contracts.md.";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Carina",
            Version = "1.0.0",
            Description = Description,
        };

        document.Servers = [new OpenApiServer { Url = "/" }];

        return Task.CompletedTask;
    }
}
