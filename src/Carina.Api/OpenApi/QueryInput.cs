using System.Text.Json.Nodes;

using Microsoft.OpenApi;

namespace Carina.Api.OpenApi;

public sealed record QueryInput
{
    private QueryInput(
        string name,
        string says,
        JsonSchemaType shape,
        string? format,
        JsonNode ordinarily,
        IReadOnlyList<string>? oneOf)
    {
        Name = name;
        Says = says;
        Shape = shape;
        Format = format;
        Ordinarily = ordinarily;
        OneOf = oneOf;
    }

    public string Name { get; }

    public string Says { get; }

    public JsonSchemaType Shape { get; }

    public string? Format { get; }

    public JsonNode Ordinarily { get; }

    public IReadOnlyList<string>? OneOf { get; }

    public static QueryInput Seconds(string name, string says)
        => new(name, says, JsonSchemaType.Number, "double", JsonValue.Create(0d), null);

    public static QueryInput OneOfThese(string name, string says, IReadOnlyList<string> values, string ordinarily)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new QueryInput(name, says, JsonSchemaType.String, null, JsonValue.Create(ordinarily), [.. values]);
    }

    public OpenApiParameter Parameter() => new()
    {
        Name = Name,
        In = ParameterLocation.Query,
        Required = false,
        Description = Says,
        Schema = new OpenApiSchema
        {
            Type = Shape,
            Format = Format,
            Default = Ordinarily.DeepClone(),
            Enum = OneOf is null ? null : [.. OneOf.Select(value => (JsonNode)value)],
        },
    };
}

public static class QueryInputs
{
    public static TBuilder Reads<TBuilder>(this TBuilder builder, params QueryInput[] inputs)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(inputs);

        builder.Add(endpoint =>
        {
            foreach (QueryInput input in inputs)
            {
                endpoint.Metadata.Add(input);
            }
        });

        return builder;
    }
}
