using System.Diagnostics;
using TheRouter.Core.Graph;

// ── Build a tiny DAG ────────────────────────────────────────────────
//
//   Start(1) ──► A(2) ──► C(4)
//            └─► B(3) ──► C(4) ──► End(5)
//
var graph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2)
    .AddEdge(1, 3)
    .AddEdge(2, 4)
    .AddEdge(3, 4)
    .AddEdge(4, 5)
    .Build();

Console.WriteLine($"Graph built: {graph.NodeCount} nodes, {graph.EdgeCount} edges");
Console.WriteLine($"Start node index: {graph.StartNodeIndex}");

// ── Warm-up ─────────────────────────────────────────────────────────
Span<int> path = stackalloc int[16];
for (int i = 0; i < 1000; i++)
    graph.TryRoute(path, out _);

// ── Allocation measurement ──────────────────────────────────────────
long before = GC.GetAllocatedBytesForCurrentThread();

const int Iterations = 50_000;
var sw = Stopwatch.StartNew();

for (int i = 0; i < Iterations; i++)
{
    // Always take the first edge (simple linear-ish walk)
    if (!graph.TryRoute(path, out int len))
        throw new Exception("Route failed");
}

sw.Stop();
long after = GC.GetAllocatedBytesForCurrentThread();
long allocated = after - before;

Console.WriteLine();
Console.WriteLine($"Iterations      : {Iterations:N0}");
Console.WriteLine($"Elapsed         : {sw.Elapsed.TotalMilliseconds:F2} ms");
Console.WriteLine($"Throughput      : {Iterations / sw.Elapsed.TotalSeconds:N0} routes/sec");
Console.WriteLine($"Allocated bytes : {allocated:N0}");
Console.WriteLine($"Bytes / route   : {(double)allocated / Iterations:F2}");

// ── Show one concrete path ──────────────────────────────────────────
graph.TryRoute(path, out int pathLen);
Console.Write("Sample path     : ");
for (int i = 0; i < pathLen; i++)
{
    ref readonly var n = ref graph.GetNode(path[i]);
    Console.Write(n.Id);
    if (i < pathLen - 1) Console.Write(" → ");
}
Console.WriteLine();
