using Carina.Domain.Base;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Conventions.Tests.Fixtures;

internal sealed class TwoActionController : ControllerBase
{
    public IActionResult Invoke() => Ok();

    public IActionResult Also() => Ok();
}

internal sealed class MisnamedActionController : ControllerBase
{
    public IActionResult Handle() => Ok();
}

internal sealed class SingleActionController : ControllerBase
{
    public IActionResult Invoke() => Ok();
}

internal sealed class StrayDependency;

internal sealed class SmugglingController(StrayDependency stray) : ControllerBase
{
    public IActionResult Invoke() => Ok(stray.ToString());
}

internal sealed class MutableTag : CommonValueObject<string>
{
    public MutableTag(string value)
        : base(value)
    {
    }

    public string? Note { get; set; }
}

internal sealed class ImmutableTag(string value) : CommonValueObject<string>(value);

internal sealed class LeakyEntity
{
    public LeakyEntity(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static LeakyEntity Rehydrate(int value) => new(value);
}

internal sealed class GuardedEntity
{
    private GuardedEntity(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static GuardedEntity Rehydrate(int value) => new(value);
}
