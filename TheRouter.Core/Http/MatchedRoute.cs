namespace TheRouter.Core.Http;

/// <summary>
/// Lightweight endpoint descriptor created only at startup, then immutable.
/// </summary>
public sealed class MatchedRoute
{
    public required string Template { get; init; }
    public required string Method { get; init; }
    public int HandlerId { get; init; }
    public object? Metadata { get; init; }

    /// <summary>
    /// Optional upstream base address for reverse-proxy scenarios.
    /// Example: "http://orders-service:8080"
    /// </summary>
    public string? UpstreamBaseAddress { get; init; }

    /// <summary>
    /// Optional path rewrite template. If null the original path is forwarded.
    /// </summary>
    public string? UpstreamPathTemplate { get; init; }

    /// <summary>Timeout for the upstream call. Null = use default.</summary>
    public TimeSpan? UpstreamTimeout { get; init; }
}
