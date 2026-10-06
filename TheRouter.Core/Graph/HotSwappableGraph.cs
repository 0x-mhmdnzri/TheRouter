using System.Runtime.CompilerServices;

namespace TheRouter.Core.Graph;

/// <summary>
/// Provides a lock-free, double-buffered view of a <see cref="RouterGraph"/>.
/// Readers always see a fully immutable snapshot; writers build a new graph
/// and publish it with a single atomic exchange.
/// </summary>
public sealed class HotSwappableGraph
{
    private RouterGraph _active;

    public HotSwappableGraph(RouterGraph initial)
    {
        _active = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    /// <summary>
    /// Returns the currently published immutable graph.
    /// Extremely cheap – just a volatile read.
    /// </summary>
    public RouterGraph Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _active);
    }

    /// <summary>
    /// Builds a brand-new graph from the supplied builder action and
    /// atomically replaces the active instance.
    /// Readers never see a partially-built graph.
    /// </summary>
    public RouterGraph Update(Action<RouterGraphBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var builder = new RouterGraphBuilder();
        build(builder);
        var next = builder.Build();

        // Atomic publish
        return Interlocked.Exchange(ref _active, next);
    }

    /// <summary>
    /// Same as <see cref="Update"/> but starts from a copy of the current
    /// topology so the caller can make incremental changes.
    /// Note: this still allocates a new graph; it does not mutate in place.
    /// </summary>
    public RouterGraph UpdateFromCurrent(Action<RouterGraphBuilder> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var current = Current;
        var builder = new RouterGraphBuilder();

        // Reconstruct the current topology into the builder
        for (int i = 0; i < current.NodeCount; i++)
        {
            ref readonly var node = ref current.GetNode(i);
            builder.AddNode(node.Id);

            var edges = current.GetOutgoingEdges(i);
            for (int e = 0; e < edges.Length; e++)
            {
                ref readonly var edge = ref edges[e];
                ref readonly var to = ref current.GetNode(edge.ToNodeIndex);
                builder.AddEdge(node.Id, to.Id, edge.Weight, edge.ConditionId);
            }
        }

        if (current.StartNodeIndex >= 0)
            builder.WithStartNode(current.GetNode(current.StartNodeIndex).Id);

        mutate(builder);
        var next = builder.Build();
        return Interlocked.Exchange(ref _active, next);
    }
}
