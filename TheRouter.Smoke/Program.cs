using System.Diagnostics;
using TheRouter.Core.Graph;
using TheRouter.Core.Graph.Selectors;
using TheRouter.Core.Http;

Console.WriteLine("=== Graph smoke ===");
var graph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2, weight: 10)
    .AddEdge(1, 3, weight: 5)
    .AddEdge(2, 4, weight: 1)
    .AddEdge(3, 4, weight: 2)
    .AddEdge(4, 5)
    .Build();

Span<int> path = stackalloc int[16];
graph.TryRoute(path, out int len, BuiltInSelectors.LowestWeight);
Console.Write("LowestWeight path: ");
for (int i = 0; i < len; i++) Console.Write(graph.GetNode(path[i]).Id + (i < len-1 ? " → " : "\n"));

Console.WriteLine("\n=== HTTP Radix matcher (absolute ranges) ===");
var matcher = new RadixTrieMatcher();
matcher.Map("GET", "/api/v1/users/{id}/items/{itemId}", new RouteEndpoint
{
    Template = "/api/v1/users/{id}/items/{itemId}",
    Method = "GET",
    HandlerId = 42
});
matcher.Freeze();

var testPath = "/api/v1/users/99/items/7";
Span<(int Start, int Length)> ranges = stackalloc (int, int)[4];
if (matcher.TryMatch("GET", testPath, out var ep, ranges, out int pc))
{
    Console.WriteLine($"Matched: {ep!.Template} (handler {ep.HandlerId})");
    for (int i = 0; i < pc; i++)
    {
        var (start, length) = ranges[i];
        var value = testPath.AsSpan(start, length);
        Console.WriteLine($"  param[{i}] = '{value}'  (start={start}, len={length})");
    }
}

// Allocation probe
long before = GC.GetAllocatedBytesForCurrentThread();
const int N = 100_000;
for (int i = 0; i < N; i++)
    matcher.TryMatch("GET", testPath, out _, ranges, out _);
long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
Console.WriteLine($"\n{N:N0} matches → allocated {alloc:N0} bytes ({(double)alloc/N:F2} B/op)");
