using System.Diagnostics;
using TheRouter.Core.Graph;
using TheRouter.Core.Graph.Selectors;

// ── Sample DAG ──────────────────────────────────────────────────────
var graph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2, weight: 10)
    .AddEdge(1, 3, weight: 5, conditionId: 1)
    .AddEdge(2, 4, weight: 1)
    .AddEdge(3, 4, weight: 2)
    .AddEdge(4, 5)
    .Build();

Console.WriteLine($"Graph: {graph.NodeCount} nodes, {graph.EdgeCount} edges\n");

Span<int> path = stackalloc int[16];

// Selectors
graph.TryRoute(path, out int len, BuiltInSelectors.First);
PrintPath("First", graph, path, len);

graph.TryRoute(path, out len, BuiltInSelectors.LowestWeight);
PrintPath("LowestWeight", graph, path, len);

// Dijkstra
Span<int> dist = stackalloc int[graph.NodeCount];
Span<int> prev = stackalloc int[graph.NodeCount];
Span<int> heap = stackalloc int[graph.NodeCount];

if (ShortestPath.TryDijkstra(graph, graph.StartNodeIndex, /*target*/ 4, dist, prev, heap, path, out len, out int cost))
    PrintPath($"Dijkstra → node 4 (cost={cost})", graph, path, len);

if (ShortestPath.TryShortestPathToAnySink(graph, dist, prev, heap, path, out len, out cost))
    PrintPath($"Dijkstra → any sink (cost={cost})", graph, path, len);

// Fan-out
Span<int> flat = stackalloc int[64];
graph.TryRouteAll(flat, out _, out int pathCount);
Console.WriteLine($"\nFan-out paths: {pathCount}");

// Hot-swappable
var store = new HotSwappableGraph(graph);
Console.WriteLine($"\nHotSwappable current nodes: {store.Current.NodeCount}");

store.Update(b =>
{
    b.WithStartNode(10)
     .AddEdge(10, 20, weight: 1)
     .AddEdge(20, 30);
});
Console.WriteLine($"After Update nodes: {store.Current.NodeCount}");

// Performance
Console.WriteLine("\n── Performance (LowestWeight) ──");
for (int i = 0; i < 2000; i++)
    graph.TryRoute(path, out _, BuiltInSelectors.LowestWeight);

long before = GC.GetAllocatedBytesForCurrentThread();
const int N = 100_000;
var sw = Stopwatch.StartNew();
for (int i = 0; i < N; i++)
    graph.TryRoute(path, out _, BuiltInSelectors.LowestWeight);
sw.Stop();
long alloc = GC.GetAllocatedBytesForCurrentThread() - before;

Console.WriteLine($"Throughput : {N / sw.Elapsed.TotalSeconds:N0} routes/sec");
Console.WriteLine($"Allocated  : {alloc:N0} bytes  ({(double)alloc / N:F2} / route)");

static void PrintPath(string label, RouterGraph g, Span<int> path, int len)
{
    Console.Write($"{label,-35}: ");
    for (int i = 0; i < len; i++)
    {
        Console.Write(g.GetNode(path[i]).Id);
        if (i < len - 1) Console.Write(" → ");
    }
    Console.WriteLine();
}
