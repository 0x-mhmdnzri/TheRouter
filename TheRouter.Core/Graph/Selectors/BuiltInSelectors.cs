using System.Runtime.CompilerServices;

namespace TheRouter.Core.Graph.Selectors;

/// <summary>
/// Ready-to-use, allocation-free edge selectors.
/// All methods are static and designed to be passed as <see cref="EdgeSelector"/> delegates
/// or used directly via method groups.
/// </summary>
public static class BuiltInSelectors
{
    /// <summary>Always picks the first outgoing edge (index 0). Fails only when the node is a sink.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int First(ReadOnlySpan<Edge> edges, int currentNodeIndex)
        => edges.Length > 0 ? 0 : -1;

    /// <summary>
    /// Picks the edge with the lowest Weight.
    /// Ties are broken by the first occurrence.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LowestWeight(ReadOnlySpan<Edge> edges, int currentNodeIndex)
    {
        if (edges.Length == 0) return -1;

        int bestIdx = 0;
        int bestWeight = edges[0].Weight;

        for (int i = 1; i < edges.Length; i++)
        {
            int w = edges[i].Weight;
            if (w < bestWeight)
            {
                bestWeight = w;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    /// <summary>
    /// Picks the edge with the highest Weight.
    /// Ties are broken by the first occurrence.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HighestWeight(ReadOnlySpan<Edge> edges, int currentNodeIndex)
    {
        if (edges.Length == 0) return -1;

        int bestIdx = 0;
        int bestWeight = edges[0].Weight;

        for (int i = 1; i < edges.Length; i++)
        {
            int w = edges[i].Weight;
            if (w > bestWeight)
            {
                bestWeight = w;
                bestIdx = i;
            }
        }

        return bestIdx;
    }
}

/// <summary>
/// Creates a conditional selector that only considers edges whose ConditionId
/// is marked as active in the supplied bit-set / mask.
/// The mask is expected to be prepared once per request (or once at startup)
/// so the hot path stays allocation-free.
/// </summary>
public sealed class ConditionalSelector
{
    private readonly ReadOnlyMemory<bool> _activeConditions;

    /// <param name="activeConditions">
    /// Index = ConditionId. Value = true means the condition is currently satisfied.
    /// ConditionId 0 is always treated as unconditional and is always allowed.
    /// </param>
    public ConditionalSelector(ReadOnlyMemory<bool> activeConditions)
    {
        _activeConditions = activeConditions;
    }

    /// <summary>
    /// EdgeSelector compatible method.
    /// Returns the index of the first edge that is either unconditional (ConditionId == 0)
    /// or whose condition is active; otherwise -1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Select(ReadOnlySpan<Edge> edges, int currentNodeIndex)
    {
        var conditions = _activeConditions.Span;

        for (int i = 0; i < edges.Length; i++)
        {
            ushort cid = edges[i].ConditionId;
            if (cid == 0)                       // unconditional
                return i;

            if (cid < conditions.Length && conditions[cid])
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Variant that also prefers lower weight among the allowed edges.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int SelectLowestWeight(ReadOnlySpan<Edge> edges, int currentNodeIndex)
    {
        var conditions = _activeConditions.Span;
        int bestIdx = -1;
        int bestWeight = int.MaxValue;

        for (int i = 0; i < edges.Length; i++)
        {
            ushort cid = edges[i].ConditionId;
            bool allowed = cid == 0 || (cid < conditions.Length && conditions[cid]);
            if (!allowed) continue;

            int w = edges[i].Weight;
            if (w < bestWeight)
            {
                bestWeight = w;
                bestIdx = i;
            }
        }

        return bestIdx;
    }
}
