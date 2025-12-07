namespace GedcomGeniSync.Services;

/// <summary>
/// Stores rate limit information from Geni API response headers
/// </summary>
public class RateLimitInfo
{
    /// <summary>
    /// Maximum number of requests allowed within a rate window
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Number of requests remaining in the current rate window
    /// </summary>
    public int? Remaining { get; set; }

    /// <summary>
    /// Window duration in seconds
    /// </summary>
    public int? WindowSeconds { get; set; }

    /// <summary>
    /// Time when the rate limit info was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Checks if we're getting close to the rate limit (less than 10% remaining)
    /// </summary>
    public bool IsNearingLimit => Remaining.HasValue && Limit.HasValue &&
                                   Remaining.Value < (Limit.Value * 0.1);

    /// <summary>
    /// Checks if the rate limit has been exceeded
    /// </summary>
    public bool IsExceeded => Remaining.HasValue && Remaining.Value <= 0;

    /// <summary>
    /// Calculates recommended delay in milliseconds based on remaining quota
    /// </summary>
    public int GetRecommendedDelayMs()
    {
        if (!Remaining.HasValue || !Limit.HasValue || !WindowSeconds.HasValue)
        {
            // No rate limit info, use conservative default (1 request per second)
            return 1000;
        }

        if (IsExceeded)
        {
            // If exceeded, wait for the full window
            return WindowSeconds.Value * 1000;
        }

        if (Remaining.Value == 0)
        {
            return WindowSeconds.Value * 1000;
        }

        // Calculate optimal delay: distribute remaining requests evenly across the window
        var timeElapsed = (DateTime.UtcNow - UpdatedAt).TotalSeconds;
        var timeRemaining = Math.Max(0, WindowSeconds.Value - timeElapsed);

        if (timeRemaining <= 0)
        {
            // Window has likely reset, use minimal delay
            return 100;
        }

        // Spread remaining requests across remaining time
        var delaySeconds = timeRemaining / Remaining.Value;
        return Math.Max(100, (int)(delaySeconds * 1000)); // Minimum 100ms
    }

    public override string ToString()
    {
        if (Limit.HasValue && Remaining.HasValue && WindowSeconds.HasValue)
        {
            return $"Rate Limit: {Remaining}/{Limit} requests remaining in {WindowSeconds}s window";
        }
        return "Rate Limit: Unknown";
    }
}
