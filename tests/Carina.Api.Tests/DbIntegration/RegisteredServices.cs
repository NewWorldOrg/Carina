using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.DbIntegration;

/// <summary>
/// Puts a stand-in around a service the product registers. The registered implementation is still the one the
/// container builds, with its own lifetime, and the stand-in is handed it to pass on to. This is the only place a
/// test builds a type behind a domain interface itself.
/// </summary>
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
