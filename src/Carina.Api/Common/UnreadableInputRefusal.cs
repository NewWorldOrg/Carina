using System.Text.Json;

using Carina.Api.Responder;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Carina.Api.Common;

public static class UnreadableInputRefusal
{
    public static IActionResult Answer(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string[] said =
        [
            .. context.ModelState
                .Where(entry => entry.Value is { Errors.Count: > 0 })
                .Select(entry => Named(entry.Key))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(name => Said(name, context.ActionDescriptor.Parameters)),
        ];

        return new BadRequestObjectResult(BaseResponder<object>.Error(
            "The request carries a value this endpoint cannot read, and what was sent is not repeated here: "
            + string.Join("; ", said)
            + "."));
    }

    private static string Named(string key)
    {
        string trimmed = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;

        return trimmed is "" or "$" ? "the body" : trimmed;
    }

    private static string Said(string name, IList<ParameterDescriptor> parameters)
    {
        string bare = name.Split('[')[0];

        ParameterDescriptor? asked = parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.BindingInfo?.BinderModelName ?? parameter.Name, bare, StringComparison.OrdinalIgnoreCase));

        return asked is not null && ChoiceOf(asked.ParameterType) is { } choice
            ? $"{bare} is one of {string.Join(", ", Enum.GetNames(choice).Select(JsonNamingPolicy.CamelCase.ConvertName))}"
            : $"{name} is not in a form this endpoint reads";
    }

    private static Type? ChoiceOf(Type type)
    {
        Type held = type.IsArray ? type.GetElementType()! : type;
        Type bare = Nullable.GetUnderlyingType(held) ?? held;

        return bare.IsEnum ? bare : null;
    }
}
