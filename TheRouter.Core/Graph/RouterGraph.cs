using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace TheRouter.Core.Graph;

/// <summary>
/// Immutable, cache-friendly DAG stored in Compressed Sparse Row (CSR) layout.
/// Designed for zero-allocation traversal on the hot path.
/// </summary>
public sealed class RouterGraph
{
    private readonly Node[] _nodes;
    private readonly Edge[] _edges;

    /// <summary>
    /// Maps domain node Id → index inside <see cref="_nodes"/>.
    /// Built once at construction time; after that the graph is fully immutable.
    /// </summary>
    private readonly FrozenDictionary<int, int> _idToIndex;

    /// <summary>Index of the designated entry / start node, or -1 if none was set.</summary>
    private readonly int _startNodeIndex;

    public int NodeCount => _nodes.Length;
    public int EdgeCount => _edges.Length;
    public int StartNodeIndex => _startNodeIndex;

    internal RouterGraph(
        Node[] nodes,
        Edge[] edges,
        FrozenDictionary<int, int> idToIndex,
        int startNodeIndex)
    {
        _nodes = nodes;
        _edges = edges;
        _idToIndex = idToIndex;
        _startNodeIndex = startNodeIndex;
    }

    /// <summary>
    /// Fast lookup of a node's index by its domain Id. Returns false if the Id is unknown.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNodeIndex(int nodeId, out int index)
        => _idToIndex.TryGetValue(nodeId, out index);

    /// <summary>
    /// Returns a read-only reference to the node at the given index.
    /// Caller must guarantee the index is in range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly Node GetNode(int index) => ref _nodes[index];

    /// <summary>
    /// Returns a read-only span of the outgoing edges of the node at <paramref name="nodeIndex"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<Edge> GetOutgoingEdges(int nodeIndex)
    {
        ref readonly var node = ref _nodes[nodeIndex];
        return _edges.AsSpan(node.FirstEdge, node.EdgeCount);
    }

    // ──────────────────────────────────────────────────────────────
    // Single-path routing (existing API, kept intact)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation path discovery.
    /// Walks the DAG starting from <paramref name="startNodeIndex"/> and writes
    /// the sequence of node indices into <paramref name="pathBuffer"/>.
    /// </summary>
    public bool TryRoute(
        int startNodeIndex,
        Span<int> pathBuffer,
        out int pathLength,
        EdgeSelector? edgeSelector = null)
    {
        pathLength = 0;

        if ((uint)startNodeIndex >= (uint)_nodes.Length)
            return false;

        int current = startNodeIndex;
        var selector = edgeSelector ?? Selectors.BuiltInSelectors.First;

        while (true)
        {
            if (pathLength >= pathBuffer.Length)
                return false;

            pathBuffer[pathLength++] = current;

            ref readonly var node = ref _nodes[current];
            if (node.IsSink)
                return true;

            ReadOnlySpan<Edge> edges = _edges.AsSpan(node.FirstEdge, node.EdgeCount);
            int chosen = selector(edges, current);

            if ((uint)chosen >= (uint)edges.Length)
                return false;

            current = edges[chosen].ToNodeIndex;
        }
    }

    /// <summary>Convenience overload that starts from the designated start node.</summary>
    public bool TryRoute(
        Span<int> pathBuffer,
        out int pathLength,
        EdgeSelector? edgeSelector = null)
    {
        if (_startNodeIndex < 0)
        {
            pathLength = 0;
            return false;
        }

        return TryRoute(_startNodeIndex, pathBuffer, out pathLength, edgeSelector);
    }

    /// <summary>Resolves a domain node Id to its index and then performs the route.</summary>
    public bool TryRouteFromId(
        int startNodeId,
        Span<int> pathBuffer,
        out int pathLength,
        EdgeSelector? edgeSelector = null)
    {
        if (!_idToIndex.TryGetValue(startNodeId, out int index))
        {
            pathLength = 0;
            return false;
        }

        return TryRoute(index, pathBuffer, out pathLength, edgeSelector);
    }

    // ──────────────────────────────────────────────────────────────
    // Fan-out: collect all paths to sinks (zero-allocation)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates every path from <paramref name="startNodeIndex"/> to a sink
    /// and writes them into a flat buffer.
    ///
    /// Layout of <paramref name="flatPathBuffer"/>:
    ///   [len0, n0_0, n0_1, ..., n0_len0-1,
    ///    len1, n1_0, n1_1, ..., n1_len1-1, ...]
    ///
    /// <paramref name="pathCount"/> receives the number of complete paths found.
    /// Returns false only when the supplied buffers are too small.
    /// </summary>
    /// <param name="startNodeIndex">Node index to start from.</param>
    /// <param name="flatPathBuffer">Flat buffer that receives all paths.</param>
    /// <param name="written">Total ints written into the flat buffer.</param>
    /// <param name="pathCount">Number of complete paths discovered.</param>
    /// <param name="edgeSelector">
    /// Optional filter. When supplied, only edges accepted by the selector are followed.
    /// When null every outgoing edge is explored (true fan-out).
    /// </param>
    /// <param name="maxDepth">Safety limit to prevent runaway recursion on very deep DAGs.</param>
    public bool TryRouteAll(
        int startNodeIndex,
        Span<int> flatPathBuffer,
        out int written,
        out int pathCount,
        EdgeSelector? edgeSelector = null,
        int maxDepth = 64)
    {
        written = 0;
        pathCount = 0;

        if ((uint)startNodeIndex >= (uint)_nodes.Length)
            return false;

        // Re-use a small stack-allocated path for the DFS walk.
        Span<int> currentPath = stackalloc int[maxDepth];
        return DfsAll(startNodeIndex, currentPath, 0, flatPathBuffer, ref written, ref pathCount, edgeSelector, maxDepth);
    }

    /// <summary>Fan-out starting from the designated start node.</summary>
    public bool TryRouteAll(
        Span<int> flatPathBuffer,
        out int written,
        out int pathCount,
        EdgeSelector? edgeSelector = null,
        int maxDepth = 64)
    {
        if (_startNodeIndex < 0)
        {
            written = 0;
            pathCount = 0;
            return false;
        }

        return TryRouteAll(_startNodeIndex, flatPathBuffer, out written, out pathCount, edgeSelector, maxDepth);
    }

    private bool DfsAll(
        int nodeIndex,
        Span<int> currentPath,
        int depth,
        Span<int> flatPathBuffer,
        ref int written,
        ref int pathCount,
        EdgeSelector? edgeSelector,
        int maxDepth)
    {
        if (depth >= maxDepth)
            return false; // safety

        currentPath[depth] = nodeIndex;
        int pathLen = depth + 1;

        ref readonly var node = ref _nodes[nodeIndex];

        if (node.IsSink)
        {
            // Need 1 + pathLen ints: length prefix + nodes
            if (written + 1 + pathLen > flatPathBuffer.Length)
                return false;

            flatPathBuffer[written++] = pathLen;
            currentPath.Slice(0, pathLen).CopyTo(flatPathBuffer.Slice(written));
            written += pathLen;
            pathCount++;
            return true;
        }

        ReadOnlySpan<Edge> edges = _edges.AsSpan(node.FirstEdge, node.EdgeCount);

        if (edgeSelector is null)
        {
            // True fan-out: explore every edge
            for (int i = 0; i < edges.Length; i++)
            {
                if (!DfsAll(edges[i].ToNodeIndex, currentPath, pathLen, flatPathBuffer, ref written, ref pathCount, null, maxDepth))
                    return false;
            }
        }
        else
        {
            // Filtered fan-out: only edges accepted by the selector
            // (selector may return different indices on successive calls,
            //  so we iterate and ask the selector for each candidate)
            for (int i = 0; i < edges.Length; i++)
            {
                // We temporarily present a single-edge span so the selector
                // can decide whether this edge is allowed.
                // A more sophisticated selector can ignore the span length.
                int decision = edgeSelector(edges.Slice(i, 1), nodeIndex);
                if (decision == 0) // accepted
                {
                    if (!DfsAll(edges[i].ToNodeIndex, currentPath, pathLen, flatPathBuffer, ref written, ref pathCount, edgeSelector, maxDepth))
                        return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Selects which outgoing edge to follow.
/// Must be allocation-free and side-effect free for use on the hot path.
/// Returns the index inside the supplied edge span, or a negative value to abort.
/// </summary>
public delegate int EdgeSelector(ReadOnlySpan<Edge> edges, int currentNodeIndex);
