# TheRouter

High-performance **DAG-based Router Engine** written in C#.

Designed for extreme throughput with near-zero GC pressure on the hot path.

## Design goals

- **> 12 000 RPS** (measured **> 4–8 M routes/sec** on modest hardware)
- Near-zero allocation on the matching / traversal path
- Cache-friendly contiguous memory layout (CSR)
- Immutable graph after construction → completely lock-free reads
- AOT-friendly core

## Features

| Feature | Status |
|---------|--------|
| Single-path routing (`TryRoute`) | ✅ |
| Weighted selection (Lowest / Highest) | ✅ |
| Conditional edges | ✅ |
| Fan-out (`TryRouteAll`) | ✅ |
| Dijkstra shortest-path (zero-alloc buffers) | ✅ |
| Hot-swappable graph (double-buffering) | ✅ |
| BenchmarkDotNet suite | ✅ |
| Minimal API sample | ✅ |

## Quick smoke test

```bash
dotnet run --project TheRouter.Smoke -c Release
```

## Benchmarks

```bash
dotnet run --project TheRouter.Benchmarks -c Release
```

Includes MemoryDiagnoser for:

- `TryRoute` (First / LowestWeight)
- Fan-out
- Dijkstra

## Core usage

```csharp
var graph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2, weight: 10)
    .AddEdge(1, 3, weight: 5)
    .AddEdge(2, 4)
    .AddEdge(3, 4)
    .Build();

// Single path
Span<int> path = stackalloc int[32];
graph.TryRoute(path, out int len, BuiltInSelectors.LowestWeight);

// Dijkstra (caller supplies working buffers → zero alloc)
Span<int> dist = stackalloc int[graph.NodeCount];
Span<int> prev = stackalloc int[graph.NodeCount];
Span<int> heap = stackalloc int[graph.NodeCount];
ShortestPath.TryDijkstra(graph, start, target, dist, prev, heap, path, out len, out int cost);

// Hot-swappable (lock-free readers)
var store = new HotSwappableGraph(graph);
store.Update(b => b.WithStartNode(10).AddEdge(10, 20));
var current = store.Current; // always an immutable snapshot
```

## Project layout

- `TheRouter.Core` – engine (CSR, selectors, Dijkstra, HotSwappable)
- `TheRouter.Smoke` – quick functional + allocation check
- `TheRouter.Benchmarks` – formal BenchmarkDotNet suite
- `Web.API` – Minimal API host

## Next possible directions

- Full topological DP shortest-path specialized for pure DAGs
- Parallel fan-out with bounded Channels
- Persistent graph snapshots / versioning
