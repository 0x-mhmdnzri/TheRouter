# TheRouter

High-performance **DAG-based Router Engine** written in C#.

Designed for extreme throughput with near-zero GC pressure on the hot path.

## Design goals

- **> 12 000 RPS** (easily exceeded – measured **> 6 M routes/sec**)
- Near-zero allocation on the matching / traversal path
- Cache-friendly contiguous memory layout (CSR – Compressed Sparse Row)
- Immutable graph after construction → completely lock-free reads
- AOT / NativeAOT friendly

## Features

| Feature | Status |
|---------|--------|
| Single-path routing (`TryRoute`) | ✅ |
| Weighted edge selection (Lowest / Highest) | ✅ |
| Conditional edges (`ConditionId` + mask) | ✅ |
| Fan-out – collect all paths (`TryRouteAll`) | ✅ |
| Minimal API sample endpoints | ✅ |
| Zero-allocation hot path | ✅ |

## Quick smoke test

```bash
dotnet run --project TheRouter.Smoke -c Release
```

Typical result:

```
Throughput      : ~6 400 000 routes/sec
Allocated bytes : ~0
```

## Usage

```csharp
var graph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2, weight: 10)
    .AddEdge(1, 3, weight: 5, conditionId: 1)
    .AddEdge(2, 4)
    .AddEdge(3, 4)
    .Build();

Span<int> path = stackalloc int[32];

// Lowest weight
graph.TryRoute(path, out int len, BuiltInSelectors.LowestWeight);

// Conditional
var conditions = new bool[4];
conditions[1] = true;
var sel = new ConditionalSelector(conditions);
graph.TryRoute(path, out len, sel.SelectLowestWeight);

// Fan-out (all paths)
Span<int> flat = stackalloc int[128];
graph.TryRouteAll(flat, out int written, out int pathCount);
```

## HTTP endpoints (Web.API)

```
GET  /route?strategy=first|lowest|highest
POST /route/all
```

## Project structure

- `TheRouter.Core` – allocation-free graph engine + selectors
- `TheRouter.Smoke` – micro-benchmark
- `Web.API` – Minimal API host with sample graph

## Next possible steps

- [ ] Dijkstra / shortest-path over the whole DAG
- [ ] Dynamic graph updates via double-buffering
- [ ] Proper BenchmarkDotNet suite
- [ ] Integration tests with WebApplicationFactory
