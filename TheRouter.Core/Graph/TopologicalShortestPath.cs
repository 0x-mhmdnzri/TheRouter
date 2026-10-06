using System.Runtime.CompilerServices;

namespace TheRouter.Core.Graph;

/// <summary>
/// Shortest-path specialized for DAGs using topological dynamic programming.
/// O(V+E) and usually faster + simpler than Dijkstra on pure DAGs.
/// All working memory is supplied by the caller → zero heap allocation on the hot path.
/// </summary>
public static class TopologicalShortestPath
{
    /// <summary>
    /// Computes single-source shortest paths on a DAG.
    /// <paramref name="topoOrder"/> must be a valid topological order of node indices
    /// (can be produced once at build time and stored).
    /// </summary>
    public static bool TryCompute(
        RouterGraph graph,
        int startIndex,
        int targetIndex,
        ReadOnlySpan<int> topoOrder,
        Span<int> dist,
        Span<int> prev,
        Span<int> pathBuffer,
        out int pathLength,
        out int cost)
    {
        pathLength = 0;
        cost = 0;
        int n = graph.NodeCount;

        if ((uint)startIndex >= (uint)n || dist.Length < n || prev.Length < n)
            return false;

        dist.Fill(int.MaxValue);
        prev.Fill(-1);
        dist[startIndex] = 0;

        for (int t = 0; t < topoOrder.Length; t++)
        {
            int u = topoOrder[t];
            int du = dist[u];
            if (du == int.MaxValue) continue;

            var edges = graph.GetOutgoingEdges(u);
            for (int i = 0; i < edges.Length; i++)
            {
                ref readonly var e = ref edges[i];
                int v = e.ToNodeIndex;
                if (du > int.MaxValue - e.Weight) continue;
                int nd = du + e.Weight;
                if (nd < dist[v])
                {
                    dist[v] = nd;
                    prev[v] = u;
                }
            }
        }

        if (targetIndex >= 0)
        {
            if (dist[targetIndex] == int.MaxValue) return false;
            cost = dist[targetIndex];
            return Reconstruct(prev, targetIndex, pathBuffer, out pathLength);
        }

        return true;
    }

    /// <summary>
    /// Builds a topological order (Kahn-style) into the supplied buffer.
    /// Returns false if the graph is not a DAG (should not happen after Builder validation).
    /// </summary>
    public static bool TryBuildTopoOrder(RouterGraph graph, Span<int> orderBuffer, out int count)
    {
        count = 0;
        int n = graph.NodeCount;
        if (orderBuffer.Length < n) return false;

        Span<int> indegree = n <= 256 ? stackalloc int[n] : new int[n];
        indegree.Clear();

        for (int i = 0; i < n; i++)
        {
            var edges = graph.GetOutgoingEdges(i);
            for (int e = 0; e < edges.Length; e++)
                indegree[edges[e].ToNodeIndex]++;
        }

        // simple queue in the order buffer itself (head/tail)
        int head = 0, tail = 0;
        for (int i = 0; i < n; i++)
            if (indegree[i] == 0)
                orderBuffer[tail++] = i;

        while (head < tail)
        {
            int u = orderBuffer[head++];
            var edges = graph.GetOutgoingEdges(u);
            for (int e = 0; e < edges.Length; e++)
            {
                int v = edges[e].ToNodeIndex;
                if (--indegree[v] == 0)
                    orderBuffer[tail++] = v;
            }
        }

        count = tail;
        return tail == n;
    }

    private static bool Reconstruct(Span<int> prev, int target, Span<int> pathBuffer, out int pathLength)
    {
        int len = 0;
        int cur = target;
        while (cur != -1)
        {
            len++;
            cur = prev[cur];
        }
        if (len > pathBuffer.Length)
        {
            pathLength = 0;
            return false;
        }
        pathLength = len;
        cur = target;
        for (int i = len - 1; i >= 0; i--)
        {
            pathBuffer[i] = cur;
            cur = prev[cur];
        }
        return true;
    }
}
