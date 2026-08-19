namespace Essessay.Options;

/// <summary>
/// Bound from the "Redis" section, plus the connection string itself — which stays in
/// its idiomatic home, ConnectionStrings:Redis, rather than moving under "Redis" too,
/// so <see cref="RedisServiceExtensions.AddRedisIfConfigured"/> populates
/// <see cref="ConnectionString"/> after binding. <see cref="IsConfigured"/> is what
/// the rest of the app asks instead of checking the string directly.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Set from ConnectionStrings:Redis; empty means "not configured".</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Lets tests (and side-by-side instances) keep their keys apart.</summary>
    public string KeyPrefix { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
