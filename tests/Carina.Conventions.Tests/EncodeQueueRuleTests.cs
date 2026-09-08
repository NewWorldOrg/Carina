using System.Reflection;

using Carina.Domain.Encodings;

namespace Carina.Conventions.Tests;

public sealed class EncodeQueueRuleTests
{
    [Fact]
    public void EncodingIsNeverGivenAWayToRegisterJobsInBulk()
        => Assert.Equal(
            [],
            typeof(IEncodeJobRepository)
                .GetMethods()
                .Where(method => method.GetParameters().Any(TakesManyJobs))
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static bool TakesManyJobs(ParameterInfo parameter)
    {
        Type shape = parameter.ParameterType;

        return shape != typeof(string)
            && shape.GetInterfaces()
                .Append(shape)
                .Any(carried => carried.IsGenericType
                    && carried.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    && carried.GetGenericArguments()[0] == typeof(EncodeJob));
    }
}
