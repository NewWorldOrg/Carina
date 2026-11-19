using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.DbIntegration;

internal static class RegisteredServices
{
    public static void Wrap<T>(IServiceCollection services, Func<T, T> wrap)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(wrap);

        ServiceDescriptor held = services.Last(descriptor => descriptor.ServiceType == typeof(T));
        Type implementation = held.ImplementationType
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered by its class, so it cannot be wrapped.");

        services.Remove(held);
        services.Add(ServiceDescriptor.Describe(
            typeof(T),
            provider => wrap((T)ActivatorUtilities.CreateInstance(provider, implementation)),
            held.Lifetime));
    }
}
