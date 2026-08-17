using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed class UnhandledFailureResponseTransformer : IOpenApiOperationTransformer
{
    private const string Failure = "500";

    private const string Json = "application/json";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Responses is not { } answers || EnvelopeOf(answers) is not { } envelope)
        {
            return Task.CompletedTask;
        }

        answers[Failure] = new OpenApiResponse
        {
            Description = "The request failed before it could answer for itself. The body carries the usual envelope with no data.",
            Content = new Dictionary<string, OpenApiMediaType> { [Json] = new() { Schema = envelope } },
        };

        return Task.CompletedTask;
    }

    private static IOpenApiSchema? EnvelopeOf(IDictionary<string, IOpenApiResponse> answers)
        => answers.Values
            .Select(answer => answer.Content is { } content && content.TryGetValue(Json, out var body)
                ? body.Schema
                : null)
            .FirstOrDefault(schema => schema is not null);
}
