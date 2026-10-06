using System.Runtime.CompilerServices;

namespace TheRouter.Core.Graph;

/// <summary>
/// Zero-allocation (caller-supplied buffers) shortest-path algorithms on the DAG.
/// </summary>
public static class ShortestPath
{
    /// <summary>
    /// Dijkstra from <paramref name="startIndex"/> to <paramref name="targetIndex"/>.
    /// All working memory is provided by the caller → no heap allocation on the hot path
    /// when buffers are rented from a pool or stackalloc'd for small graphs.
    /// </summary>
    /// <param name="graph">The immutable graph.</param>
    /// <param name="startIndex">Source node index.</param>
    /// <param name="targetIndex">Destination node index (-1 = find path to any sink / just compute distances).</param>
    /// <param name="dist">Distance array (length >= NodeCount). Will be filled with best distances.</param>
    /// <param name="prev">Predecessor array (length >= NodeCount). Used to reconstruct the path.</param>
    /// <param name="heap">Binary-heap storage (length >= NodeCount). Used as priority queue.</param>
    /// <param name="pathBuffer">Receives the resulting path (node indices) from start to target.</param>
    /// <param name="pathLength">Number of nodes written into pathBuffer.</param>
    /// <param name="cost">Total cost of the found path.</param>
    /// <returns>true if a path was found (or distances were computed when targetIndex == -1).</returns>
    public static bool TryDijkstra(
        RouterGraph graph,
        int startIndex,
        int targetIndex,
        Span<int> dist,
        Span<int> prev,
        Span<int> heap,
        Span<int> pathBuffer,
        out int pathLength,
        out int cost)
    {
        pathLength = 0;
        cost = 0;

        int n = graph.NodeCount;
        if ((uint)startIndex >= (uint)n ||
            dist.Length < n || prev.Length < n || heap.Length < n)
            return false;

        // Initialize
        dist.Fill(int.MaxValue);
        prev.Fill(-1);
        dist[startIndex] = 0;

        // Binary min-heap of node indices, keyed by dist[]
        int heapSize = 0;
        HeapPush(heap, ref heapSize, startIndex, dist);

        while (heapSize > 0)
        {
            int u = HeapPop(heap, ref heapSize, dist);

            if (u == targetIndex)
                break; // early exit when target reached

            int du = dist[u];
            if (du == int.MaxValue)
                break;

            var edges = graph.GetOutgoingEdges(u);
            for (int i = 0; i < edges.Length; i++)
            {
                ref readonly var e = ref edges[i];
                int v = e.ToNodeIndex;
                int nd = du + e.Weight;

                // protect against overflow
                if (du > int.MaxValue - e.Weight)
                    continue;

                if (nd < dist[v])
                {
                    dist[v] = nd;
                    prev[v] = u;
                    HeapPush(heap, ref heapSize, v, dist);
                }
            }
        }

        if (targetIndex >= 0)
        {
            if (dist[targetIndex] == int.MaxValue)
                return false;

            // Reconstruct path (reverse then reverse again)
            cost = dist[targetIndex];
            int cur = targetIndex;
            int len = 0;
            // First count length
            while (cur != -1)
            {
                len++;
                cur = prev[cur];
            }

            if (len > pathBuffer.Length)
                return false;

            pathLength = len;
            cur = targetIndex;
            for (int i = len - 1; i >= 0; i--)
            {
                pathBuffer[i] = cur;
                cur = prev[cur];
            }
            return true;
        }

        // targetIndex == -1 → only distances requested
        return true;
    }

    /// <summary>
    /// Convenience wrapper that finds the shortest path from the graph's start node
    /// to the first sink that is reachable with finite cost.
    /// </summary>
    public static bool TryShortestPathToAnySink(
        RouterGraph graph,
        Span<int> dist,
        Span<int> prev,
        Span<int> heap,
        Span<int> pathBuffer,
        out int pathLength,
        out int cost)
    {
        pathLength = 0;
        cost = 0;

        if (graph.StartNodeIndex < 0)
            return false;

        if (!TryDijkstra(graph, graph.StartNodeIndex, -1, dist, prev, heap, pathBuffer, out _, out _))
            return false;

        // Find the reachable sink with the smallest distance
        int bestSink = -1;
        int bestCost = int.MaxValue;

        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (graph.GetNode(i).IsSink && dist[i] < bestCost)
            {
                bestCost = dist[i];
                bestSink = i;
            }
        }

        if (bestSink < 0)
            return false;

        // Reconstruct
        cost = bestCost;
        int cur = bestSink;
        int len = 0;
        while (cur != -1)
        {
            len++;
            cur = prev[cur];
        }

        if (len > pathBuffer.Length)
            return false;

        pathLength = len;
        cur = bestSink;
        for (int i = len - 1; i >= 0; i--)
        {
            pathBuffer[i] = cur;
            cur = prev[cur];
        }
        return true;
    }

    // ── Minimal binary heap (node indices, ordered by dist[]) ────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HeapPush(Span<int> heap, ref int size, int node, Span<int> dist)
    {
        int i = size++;
        heap[i] = node;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (dist[heap[parent]] <= dist[heap[i]])
                break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HeapPop(Span<int> heap, ref int size, Span<int> dist)
    {
        int root = heap[0];
        heap[0] = heap[--size];

        int i = 0;
        while (true)
        {
            int left = 2 * i + 1;
            int right = left + 1;
            int smallest = i;

            if (left < size && dist[heap[left]] < dist[heap[smallest]])
                smallest = left;
            if (right < size && dist[heap[right]] < dist[heap[smallest]])
                smallest = right;

            if (smallest == i)
                break;

            (heap[i], heap[smallest]) = (heap[smallest], heap[i]);
            i = smallest;
        }

        return root;
    }
}
