using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Carina.Api.Authentication;
using Carina.Api.Tests.Unit;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

internal static class ContractAgreement
{
    public static IEnumerable<string> Disagreements(
        JsonNode document,
        string path,
        string method,
        HttpStatusCode status,
        string body)
    {
        JsonObject? answers = document["paths"]?[path]?[method.ToLowerInvariant()]?["responses"]?.AsObject();
        string named = ((int)status).ToString(CultureInfo.InvariantCulture);

        if (answers is null)
        {
            return [$"{method} {path} answered {named} and is not described at all"];
        }

        if (answers[named] is not { } described)
        {
            return [$"{method} {path} answered {named}, which is not among {Described(answers)}"];
        }

        bool describesABody = described["content"] is not null;
        bool carriesABody = body.Length > 0;

        if (describesABody == carriesABody)
        {
            return [];
        }

        return
        [
            describesABody
                ? $"{method} {path} answered {named} with nothing, though a body is described"
                : $"{method} {path} answered {named} carrying a body, though none is described",
        ];
    }

    private static string Described(JsonObject answers)
        => string.Join(", ", answers.Select(answer => answer.Key).Order(StringComparer.Ordinal));
}
