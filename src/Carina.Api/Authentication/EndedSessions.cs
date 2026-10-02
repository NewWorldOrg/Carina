using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

/// <summary>
/// The sessions a request ended: how many, and for one ended by name, how it had signed in and on which device.
/// </summary>
public sealed record EndedSessions(int Count, AuthMethod? Method = null, string? Device = null);
