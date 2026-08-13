namespace Carina.Db.Tests;

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string name;
    private readonly string? original;

    public EnvironmentVariableScope(string name, string? value)
    {
        this.name = name;
        original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(name, original);
}
