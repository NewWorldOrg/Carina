using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

public sealed record EndedSessions(int Count, AuthMethod? Method = null, string? Device = null);
