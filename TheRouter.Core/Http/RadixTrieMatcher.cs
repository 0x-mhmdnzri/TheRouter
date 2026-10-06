using System.Runtime.CompilerServices;

namespace TheRouter.Core.Http;

/// <summary>
/// Immutable, allocation-conscious HTTP path matcher built as a segment trie.
/// Matching works exclusively with ReadOnlySpan&lt;char&gt; and never allocates
/// on the success/failure hot path when the HTTP method is one of the well-known values.
/// Parameter values are returned as absolute (start, length) ranges into the original path.
/// </summary>
public sealed class RadixTrieMatcher
{
    private readonly RouteNode _root = new();
    private bool _frozen;

    public void Map(string method, string template, RouteEndpoint endpoint)
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

        node.RouteEndpoints ??= new Dictionary<string, RouteEndpoint>(StringComparer.OrdinalIgnoreCase);
        node.RouteEndpoints[method] = endpoint;
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
    /// Zero-allocation match. Parameter ranges are absolute offsets into <paramref name="path"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryMatch(
        ReadOnlySpan<char> method,
        ReadOnlySpan<char> path,
        out RouteEndpoint? endpoint,
        Span<(int Start, int Length)> paramRanges,
        out int paramCount)
    {
        endpoint = null;
        paramCount = 0;

        if (!_frozen)
            throw new InvalidOperationException("Call Freeze() before matching.");

        // Keep original for absolute offsets
        ReadOnlySpan<char> original = path;
        int baseOffset = 0;

        // Manual trim so we can track offset
        if (path.Length > 0 && path[0] == '/')
        {
            path = path[1..];
            baseOffset = 1;
        }
        if (path.Length > 0 && path[^1] == '/')
            path = path[..^1];

        var node = _root;
        int consumed = 0; // relative to the trimmed path

        while (!path.IsEmpty)
        {
            int slash = path.IndexOf('/');
            var segment = slash < 0 ? path : path[..slash];
            int segmentStartInOriginal = baseOffset + consumed;
            int segmentLen = segment.Length;

            var remaining = slash < 0 ? default : path[(slash + 1)..];
            int advance = slash < 0 ? path.Length : slash + 1;

            if (node.StaticChildren.Count > 0 &&
                node.StaticLookup.TryGetValue(segment, out var staticChild))
            {
                node = staticChild;
                path = remaining;
                consumed += advance;
                continue;
            }

            if (node.ParameterChild is { } paramNode)
            {
                if (paramCount < paramRanges.Length)
                    paramRanges[paramCount++] = (segmentStartInOriginal, segmentLen);

                node = paramNode;
                path = remaining;
                consumed += advance;
                continue;
            }

            if (node.CatchAllChild is { } catchNode)
            {
                // rest of the path (already trimmed of leading slash logic)
                int catchStart = baseOffset + consumed;
                int catchLen = original.Length - catchStart;
                // strip trailing slash if we trimmed it earlier
                if (catchLen > 0 && original[catchStart + catchLen - 1] == '/' && 
                    (baseOffset + consumed + path.Length < original.Length))
                {
                    // already handled by earlier trim
                }

                if (paramCount < paramRanges.Length)
                    paramRanges[paramCount++] = (catchStart, Math.Max(0, original.Length - catchStart));

                node = catchNode;
                break;
            }

            return false;
        }

        if (node.RouteEndpoints is null)
            return false;

        return TryGetRouteEndpoint(node.RouteEndpoints, method, out endpoint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryMatch(ReadOnlySpan<char> method, ReadOnlySpan<char> path, out RouteEndpoint? endpoint)
    {
        Span<(int, int)> dummy = stackalloc (int, int)[8];
        return TryMatch(method, path, out endpoint, dummy, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetRouteEndpoint(
        Dictionary<string, RouteEndpoint> map,
        ReadOnlySpan<char> method,
        out RouteEndpoint? endpoint)
    {
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

        return map.TryGetValue(method.ToString(), out endpoint);
    }
}
