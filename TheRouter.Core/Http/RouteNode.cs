namespace TheRouter.Core.Http;

/// <summary>
/// Node in the radix / segment trie.
/// Static children are stored in a Dictionary that later gets an AlternateLookup
/// for zero-allocation span-based lookup.
/// </summary>
internal sealed class RouteNode
{
    public readonly Dictionary<string, RouteNode> StaticChildren = new(StringComparer.Ordinal);

    /// <summary>
    /// Set once after the tree is fully built. Allows TryGetValue with ReadOnlySpan&lt;char&gt;.
    /// </summary>
    public Dictionary<string, RouteNode>.AlternateLookup<ReadOnlySpan<char>> StaticLookup;

    /// <summary>Single parameter child (e.g. {id}). Only one is allowed per node for simplicity.</summary>
    public RouteNode? ParameterChild;

    /// <summary>Name of the parameter (without braces), stored only for diagnostics / binding.</summary>
    public string? ParameterName;

    /// <summary>Catch-all child (e.g. {*path}).</summary>
    public RouteNode? CatchAllChild;

    public string? CatchAllName;

    /// <summary>MatchedRoute that terminates at this node (method-specific endpoints live in a small map).</summary>
    public Dictionary<string, MatchedRoute>? MatchedRoutes; // method → endpoint
}
