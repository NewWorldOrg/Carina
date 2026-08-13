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
        int statusCode,
        DriverProblem? problem,
        string? failure)
    {
        Outcome = outcome;
        Value = value;
        StatusCode = statusCode;
        Problem = problem;
        Failure = failure;
    }

    public DriverCallOutcome Outcome { get; }

    public T? Value { get; }

    public int StatusCode { get; }

    public DriverProblem? Problem { get; }

    public string? Failure { get; }

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = Value;

        return Outcome is DriverCallOutcome.Reached && value is not null;
    }

    public static DriverCall<T> Reached(T? value, int statusCode)
        => new(DriverCallOutcome.Reached, value, statusCode, null, null);

    public static DriverCall<T> Refused(int statusCode, DriverProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return new DriverCall<T>(DriverCallOutcome.Refused, default, statusCode, problem, null);
    }

    public static DriverCall<T> Unreachable(string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        return new DriverCall<T>(DriverCallOutcome.Unreachable, default, 0, null, failure);
    }
}
