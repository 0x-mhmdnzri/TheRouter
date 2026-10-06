namespace TheRouter.Core.Graph;

/// <summary>
/// Contiguous, allocation-free representation of a directed edge.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct Edge
{
    /// <summary>Index of the destination node inside the nodes array (not the domain Id).</summary>
    public readonly int ToNodeIndex;

    /// <summary>Optional weight used for selection / shortest-path style decisions.</summary>
    public readonly int Weight;

    /// <summary>
    /// Optional condition identifier. 0 = unconditional.
    /// Higher layers can map this to a predicate without allocating on the hot path.
    /// </summary>
    public readonly ushort ConditionId;

    public Edge(int toNodeIndex, int weight = 0, ushort conditionId = 0)
    {
        ToNodeIndex = toNodeIndex;
        Weight = weight;
        ConditionId = conditionId;
    }
}
