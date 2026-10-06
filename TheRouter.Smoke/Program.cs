using System.Diagnostics;
using TheRouter.Core.Graph;
using TheRouter.Core.Graph.Selectors;

// ── Build a small DAG with weights & conditions ─────────────────────
//
//          ┌─(w=10)─► A(2) ─(w=1)─► C(4) ─► End(5)
//  Start(1)┤
//          └─(w=5, cond=1)─► B(3) ─(w=2)─► C(4) ─► End(5)
//
var graph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2, weight: 10)
    .AddEdge(1, 3, weight: 5, conditionId: 1)
    .AddEdge(2, 4, weight: 1)
    .AddEdge(3, 4, weight: 2)
    .AddEdge(4, 5)
    .Build();

Console.WriteLine($"Graph: {graph.NodeCount} nodes, {graph.EdgeCount} edges");
Console.WriteLine();

Span<int> path = stackalloc int[16];

// ── 1. First-edge (default) ─────────────────────────────────────────
graph.TryRoute(path, out int len, BuiltInSelectors.First);
PrintPath("First edge", graph, path, len);

// ── 2. Lowest weight ────────────────────────────────────────────────
graph.TryRoute(path, out len, BuiltInSelectors.LowestWeight);
PrintPath("Lowest weight", graph, path, len);

// ── 3. Conditional (condition 1 active) ──────────────────────────────
var conditions = new bool[4]; // index = ConditionId
conditions[1] = true;
var conditional = new ConditionalSelector(conditions);
graph.TryRoute(path, out len, conditional.Select);
PrintPath("Conditional (cond 1 ON)", graph, path, len);

// ── 4. Conditional + lowest weight ──────────────────────────────────
graph.TryRoute(path, out len, conditional.SelectLowestWeight);
PrintPath("Conditional + LowestWeight", graph, path, len);

// ── 5. Fan-out (all paths) ───────────────────────────────────────────
Span<int> flat = stackalloc int[64];
graph.TryRouteAll(flat, out int written, out int pathCount);
Console.WriteLine($"\nFan-out: {pathCount} paths, {written} ints written");
int cursor = 0;
for (int p = 0; p < pathCount; p++)
{
    int plen = flat[cursor++];
    Console.Write($"  path[{p}]: ");
    for (int i = 0; i < plen; i++)
    {
        Console.Write(graph.GetNode(flat[cursor++]).Id);
        if (i < plen - 1) Console.Write(" → ");
    }
    Console.WriteLine();
}

// ── Throughput + allocation ─────────────────────────────────────────
Console.WriteLine("\n── Performance ──");
for (int i = 0; i < 2000; i++) // warm-up
    graph.TryRoute(path, out _, BuiltInSelectors.LowestWeight);

long before = GC.GetAllocatedBytesForCurrentThread();
const int Iterations = 100_000;
var sw = Stopwatch.StartNew();

for (int i = 0; i < Iterations; i++)
    graph.TryRoute(path, out _, BuiltInSelectors.LowestWeight);

sw.Stop();
long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

Console.WriteLine($"Iterations      : {Iterations:N0}");
Console.WriteLine($"Elapsed         : {sw.Elapsed.TotalMilliseconds:F2} ms");
Console.WriteLine($"Throughput      : {Iterations / sw.Elapsed.TotalSeconds:N0} routes/sec");
Console.WriteLine($"Allocated bytes : {allocated:N0}");
Console.WriteLine($"Bytes / route   : {(double)allocated / Iterations:F2}");

static void PrintPath(string label, RouterGraph g, Span<int> path, int len)
{
    Console.Write($"{label,-30}: ");
    for (int i = 0; i < len; i++)
    {
        Console.Write(g.GetNode(path[i]).Id);
        if (i < len - 1) Console.Write(" → ");
    }
    Console.WriteLine();
}
