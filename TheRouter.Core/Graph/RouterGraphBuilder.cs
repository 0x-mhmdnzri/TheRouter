using System.Collections.Frozen;

namespace TheRouter.Core.Graph;

/// <summary>
/// Builds an immutable <see cref="RouterGraph"/> from a mutable description.
/// Validation (including basic cycle detection) happens here so the hot path stays clean.
/// </summary>
public sealed class RouterGraphBuilder
{
    private readonly Dictionary<int, List<EdgeDefinition>> _adjacency = new();
    private int? _startNodeId;

    /// <summary>Registers a node. Nodes that never receive an edge are still valid sinks.</summary>
    public RouterGraphBuilder AddNode(int nodeId)
    {
        if (!_adjacency.ContainsKey(nodeId))
            _adjacency[nodeId] = new List<EdgeDefinition>();
        return this;
    }

    /// <summary>Adds a directed edge. Both endpoints are auto-registered if missing.</summary>
    public RouterGraphBuilder AddEdge(int fromId, int toId, int weight = 0, ushort conditionId = 0)
    {
        AddNode(fromId);
        AddNode(toId);
        _adjacency[fromId].Add(new EdgeDefinition(toId, weight, conditionId));
        return this;
    }

    /// <summary>Designates the entry point of the graph.</summary>
    public RouterGraphBuilder WithStartNode(int nodeId)
    {
        AddNode(nodeId);
        _startNodeId = nodeId;
        return this;
    }

    /// <summary>
    /// Materializes the immutable CSR graph.
    /// Throws <see cref="InvalidOperationException"/> when a cycle is detected.
    /// </summary>
    public RouterGraph Build()
    {
        if (_adjacency.Count == 0)
            throw new InvalidOperationException("Graph contains no nodes.");

        // Stable ordering: sort by Id so the resulting arrays are deterministic.
        var orderedIds = _adjacency.Keys.OrderBy(id => id).ToArray();
        var idToIndex = new Dictionary<int, int>(orderedIds.Length);

        for (int i = 0; i < orderedIds.Length; i++)
            idToIndex[orderedIds[i]] = i;

        // First pass: count total edges and fill Node array.
        var nodes = new Node[orderedIds.Length];
        int totalEdges = 0;

        for (int i = 0; i < orderedIds.Length; i++)
        {
            int id = orderedIds[i];
            var outs = _adjacency[id];
            nodes[i] = new Node(id, totalEdges, outs.Count);
            totalEdges += outs.Count;
        }

        // Second pass: fill Edge array.
        var edges = new Edge[totalEdges];
        int edgeCursor = 0;

        for (int i = 0; i < orderedIds.Length; i++)
        {
            int id = orderedIds[i];
            foreach (var def in _adjacency[id])
            {
                if (!idToIndex.TryGetValue(def.ToId, out int toIndex))
                    throw new InvalidOperationException($"Edge target {def.ToId} is unknown.");

                edges[edgeCursor++] = new Edge(toIndex, def.Weight, def.ConditionId);
            }
        }

        // Basic cycle detection via DFS coloring.
        if (HasCycle(nodes, edges))
            throw new InvalidOperationException("Graph contains a cycle; only DAGs are supported.");

        int startIndex = -1;
        if (_startNodeId is int startId)
        {
            if (!idToIndex.TryGetValue(startId, out startIndex))
                throw new InvalidOperationException($"Start node {startId} was never registered.");
        }

        var frozenMap = idToIndex.ToFrozenDictionary();

        return new RouterGraph(nodes, edges, frozenMap, startIndex);
    }

    private static bool HasCycle(Node[] nodes, Edge[] edges)
    {
        // 0 = unvisited, 1 = visiting, 2 = visited
        var state = new byte[nodes.Length];

        for (int i = 0; i < nodes.Length; i++)
        {
            if (state[i] == 0 && Dfs(i))
                return true;
        }

        return false;

        bool Dfs(int nodeIndex)
        {
            state[nodeIndex] = 1; // visiting

            ref readonly var node = ref nodes[nodeIndex];
            var span = edges.AsSpan(node.FirstEdge, node.EdgeCount);

            foreach (ref readonly var edge in span)
            {
                byte s = state[edge.ToNodeIndex];
                if (s == 1) return true;          // back-edge → cycle
                if (s == 0 && Dfs(edge.ToNodeIndex)) return true;
            }

            state[nodeIndex] = 2; // visited
            return false;
        }
    }

    private readonly record struct EdgeDefinition(int ToId, int Weight, ushort ConditionId);
}
