namespace TheRouter.Core.Http;

/// <summary>
/// Lightweight endpoint descriptor. Kept as a class because it is created
/// only at startup and then becomes immutable.
/// </summary>
public sealed class Endpoint
{
    public required string Template { get; init; }
    public required string Method { get; init; }
    public object? Metadata { get; init; }

    /// <summary>Optional handler id or delegate reference for later dispatch.</summary>
    public int HandlerId { get; init; }
}
