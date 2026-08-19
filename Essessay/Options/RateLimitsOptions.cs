using System.ComponentModel.DataAnnotations;

namespace Essessay.Options;

/// <summary>
/// Bound from the "RateLimits" section. Configuration so the test host can loosen
/// these out of the way, and one test can tighten them to prove each limiter actually
/// runs — see RateLimitTests. Validated on start so a mistyped value (a stray "-5")
/// fails at startup with a clear message instead of misbehaving silently at runtime.
/// </summary>
public sealed class RateLimitsOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Concurrent board streams a client may hold open at once.</summary>
    [Range(1, int.MaxValue)]
    public int BoardStreamsPerClient { get; set; } = 8;

    /// <summary>Requests to the Identity pages a client may make per minute.</summary>
    [Range(1, int.MaxValue)]
    public int IdentityRequestsPerMinute { get; set; } = 60;

    /// <summary>Card adds, moves and deletes a client may make per minute.</summary>
    [Range(1, int.MaxValue)]
    public int BoardCardsPerMinute { get; set; } = 30;
}
