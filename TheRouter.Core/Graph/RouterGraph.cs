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

    /// <summary>
    /// Zero-allocation path discovery.
    /// Walks the DAG starting from <paramref name="startNodeIndex"/> and writes
    /// the sequence of node indices into <paramref name="pathBuffer"/>.
    /// </summary>
    /// <param name="startNodeIndex">Index of the node to start from.</param>
    /// <param name="pathBuffer">Pre-allocated buffer that receives the path (node indices).</param>
    /// <param name="pathLength">Number of nodes written into the buffer.</param>
    /// <param name="edgeSelector">
    /// Optional delegate that chooses which outgoing edge to follow.
    /// When null the first edge is taken (useful for simple linear routes).
    /// The selector must itself be allocation-free.
    /// </param>
    /// <returns>true when a sink was reached; false on dead-end or buffer overflow.</returns>
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

        while (true)
        {
            if (pathLength >= pathBuffer.Length)
                return false; // buffer overflow – caller must supply a larger buffer

            pathBuffer[pathLength++] = current;

            ref readonly var node = ref _nodes[current];

            if (node.IsSink)
                return true;

            ReadOnlySpan<Edge> edges = _edges.AsSpan(node.FirstEdge, node.EdgeCount);

            int chosenEdgeIndex = edgeSelector is null
                ? 0
                : edgeSelector(edges, current);

            if ((uint)chosenEdgeIndex >= (uint)edges.Length)
                return false; // selector rejected all edges or returned invalid index

            current = edges[chosenEdgeIndex].ToNodeIndex;
        }
    }

    /// <summary>
    /// Convenience overload that starts from the designated start node.
    /// </summary>
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

    /// <summary>
    /// Resolves a domain node Id to its index and then performs the route.
    /// </summary>
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
}

/// <summary>
/// Selects which outgoing edge to follow.
/// Must be allocation-free and side-effect free for use on the hot path.
/// Returns the index inside the supplied edge span, or a negative value to abort.
/// </summary>
public delegate int EdgeSelector(ReadOnlySpan<Edge> edges, int currentNodeIndex);
