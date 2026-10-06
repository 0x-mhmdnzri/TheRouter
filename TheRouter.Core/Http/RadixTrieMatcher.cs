using System.Runtime.CompilerServices;

namespace TheRouter.Core.Http;

/// <summary>
/// Immutable, allocation-conscious HTTP path matcher built as a segment trie.
/// Matching works exclusively with ReadOnlySpan&lt;char&gt; and never allocates
/// on the success/failure hot path when the HTTP method is one of the well-known values.
/// </summary>
public sealed class RadixTrieMatcher
{
    private readonly RouteNode _root = new();
    private bool _frozen;

    public void Map(string method, string template, Endpoint endpoint)
    {
        if (_frozen)
            throw new InvalidOperationException("Matcher is frozen; cannot add more routes.");

        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(template);

        var node = _root;
        var path = template.AsSpan().Trim('/');

        while (!path.IsEmpty)
        {
            int slash = path.IndexOf('/');
            var segment = slash < 0 ? path : path[..slash];
            path = slash < 0 ? default : path[(slash + 1)..];

            if (segment.IsEmpty) continue;

            if (segment[0] == '{')
            {
                bool catchAll = segment.Length > 1 && segment[1] == '*';
                var name = catchAll
                    ? segment[2..^1].ToString()
                    : segment[1..^1].ToString();

                if (catchAll)
                {
                    node.CatchAllChild ??= new RouteNode();
                    node.CatchAllName = name;
                    node = node.CatchAllChild;
                    break;
                }
                else
                {
                    node.ParameterChild ??= new RouteNode { ParameterName = name };
                    node = node.ParameterChild;
                }
            }
            else
            {
                var key = segment.ToString();
                if (!node.StaticChildren.TryGetValue(key, out var child))
                {
                    child = new RouteNode();
                    node.StaticChildren[key] = child;
                }
                node = child;
            }
        }

        node.Endpoints ??= new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
        node.Endpoints[method] = endpoint;
    }

    public void Freeze()
    {
        if (_frozen) return;
        FreezeNode(_root);
        _frozen = true;
    }

    private static void FreezeNode(RouteNode node)
    {
        if (node.StaticChildren.Count > 0)
            node.StaticLookup = node.StaticChildren.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var child in node.StaticChildren.Values)
            FreezeNode(child);

        if (node.ParameterChild is not null)
            FreezeNode(node.ParameterChild);

        if (node.CatchAllChild is not null)
            FreezeNode(node.CatchAllChild);
    }

    /// <summary>
    /// Zero-allocation match for the common case (known HTTP methods).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryMatch(
        ReadOnlySpan<char> method,
        ReadOnlySpan<char> path,
        out Endpoint? endpoint,
        Span<(int Start, int Length)> paramRanges,
        out int paramCount)
    {
        endpoint = null;
        paramCount = 0;

        if (!_frozen)
            throw new InvalidOperationException("Call Freeze() before matching.");

        var node = _root;
        path = TrimSlashes(path);

        while (!path.IsEmpty)
        {
            int slash = path.IndexOf('/');
            var segment = slash < 0 ? path : path[..slash];
            var remaining = slash < 0 ? default : path[(slash + 1)..];

            if (node.StaticChildren.Count > 0 &&
                node.StaticLookup.TryGetValue(segment, out var staticChild))
            {
                node = staticChild;
                path = remaining;
                continue;
            }

            if (node.ParameterChild is { } paramNode)
            {
                if (paramCount < paramRanges.Length)
                    paramRanges[paramCount++] = (0, segment.Length); // relative length; absolute offset can be added later
                node = paramNode;
                path = remaining;
                continue;
            }

            if (node.CatchAllChild is { } catchNode)
            {
                if (paramCount < paramRanges.Length)
                    paramRanges[paramCount++] = (0, path.Length);
                node = catchNode;
                break;
            }

            return false;
        }

        if (node.Endpoints is null)
            return false;

        // Zero-alloc method lookup for the 7 common verbs
        return TryGetEndpoint(node.Endpoints, method, out endpoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryMatch(ReadOnlySpan<char> method, ReadOnlySpan<char> path, out Endpoint? endpoint)
    {
        Span<(int, int)> dummy = stackalloc (int, int)[8];
        return TryMatch(method, path, out endpoint, dummy, out _);
    }

    /// <summary>
    /// Avoids method.ToString() for the well-known HTTP methods.
    /// Falls back to ToString only for exotic methods (rare).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetEndpoint(
        Dictionary<string, Endpoint> map,
        ReadOnlySpan<char> method,
        out Endpoint? endpoint)
    {
        // Fast path for the methods that appear in 99.9 % of traffic
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("GET", out endpoint);
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("POST", out endpoint);
        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("PUT", out endpoint);
        if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("DELETE", out endpoint);
        if (method.Equals("PATCH", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("PATCH", out endpoint);
        if (method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("HEAD", out endpoint);
        if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            return map.TryGetValue("OPTIONS", out endpoint);

        // Exotic method – one allocation is acceptable
        return map.TryGetValue(method.ToString(), out endpoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> TrimSlashes(ReadOnlySpan<char> path)
    {
        if (path.Length > 0 && path[0] == '/')
            path = path[1..];
        if (path.Length > 0 && path[^1] == '/')
            path = path[..^1];
        return path;
    }
}
