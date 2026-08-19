namespace Essessay.Services;

/// <summary>Policy names, so the controller attribute and Program.cs cannot disagree.</summary>
public static class RateLimits
{
    /// <summary>Caps concurrent SSE streams per client, not requests per second.</summary>
    public const string BoardStream = "board-stream";

    /// <summary>Everything under /Identity: password guesses and outbound email.</summary>
    public const string Identity = "identity";

    /// <summary>Adding, moving and deleting cards — the requests that grow the board.</summary>
    public const string BoardCards = "board-cards";
}
