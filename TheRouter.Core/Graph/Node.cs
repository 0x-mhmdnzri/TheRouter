namespace TheRouter.Core.Graph;

/// <summary>
/// Contiguous, allocation-free representation of a graph node.
/// Layout is sequential for maximum cache locality.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct Node
{
    /// <summary>Stable domain identifier of the node.</summary>
    public readonly int Id;

    /// <summary>Index into the edges array where this node's outgoing edges begin.</summary>
    public readonly int FirstEdge;

    /// <summary>Number of outgoing edges.</summary>
    public readonly int EdgeCount;

    public Node(int id, int firstEdge, int edgeCount)
    {
        Id = id;
        FirstEdge = firstEdge;
        EdgeCount = edgeCount;
    }

    public bool IsSink => EdgeCount == 0;
}
