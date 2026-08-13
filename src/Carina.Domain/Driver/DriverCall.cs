using System.Diagnostics.CodeAnalysis;

using Carina.Contracts;

namespace Carina.Domain.Driver;

public enum DriverCallOutcome
{
    Unreachable,
    Reached,
    Refused,
}

public sealed class DriverCall<T>
{
    private DriverCall(
        DriverCallOutcome outcome,
        T? value,
        DriverProblem? problem,
        string? failure)
    {
        Outcome = outcome;
        Value = value;
        Problem = problem;
        Failure = failure;
    }

    public DriverCallOutcome Outcome { get; }

    public T? Value { get; }

    public DriverProblem? Problem { get; }

    public string? Failure { get; }

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = Value;

        return Outcome is DriverCallOutcome.Reached && value is not null;
    }

    public static DriverCall<T> Reached(T? value)
        => new(DriverCallOutcome.Reached, value, null, null);

    public static DriverCall<T> Refused(DriverProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return new DriverCall<T>(DriverCallOutcome.Refused, default, problem, null);
    }

    public static DriverCall<T> Unreachable(string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        return new DriverCall<T>(DriverCallOutcome.Unreachable, default, null, failure);
    }
}
