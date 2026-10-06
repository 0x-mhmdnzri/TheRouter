# TheRouter

High-performance routing engine in C# (DAG graph + HTTP radix-trie matcher).

## HTTP Radix-Trie Matcher (new)

Designed according to the principles:

- Immutable route table (build → Freeze → lock-free reads)
- `ReadOnlySpan<char>` + `AlternateLookup` (no string per segment)
- Zero allocation on the hot path for the 7 common HTTP methods
- Parameters returned as ranges, not strings

### Horrible test case results (before → after fix)

| Metric | Before (method.ToString) | After (span method lookup) |
|--------|--------------------------|----------------------------|
| Throughput | ~1.2 M matches/sec | ~1.4 M matches/sec |
| Allocated | **25.6 B / op** | **~0 B / op** |
| Total for 200 k ops | 5.1 MB | ~40 B |

The single `method.ToString()` inside the match loop was enough to destroy the allocation profile.

### Run the formal suite

```bash
dotnet run --project TheRouter.Benchmarks -c Release
```

## DAG Engine (previous work)

Still present under `TheRouter.Core/Graph` – zero-alloc CSR graph, Dijkstra, HotSwappable, etc.

## Design rules that matter for 12 k+ RPS

1. **Immutable snapshot** of the route table – swap with `Interlocked.Exchange` / `Volatile.Write`.
2. **Radix / segment trie + Span** – never Split, never regex, never LINQ on the hot path.
3. **Eliminate every allocation** on the match path (the method.ToString bug was a classic example).
4. **Proxy / forward** is usually the real cost – use a shared `SocketsHttpHandler`, stream the body, prefer YARP if you do not want to own that layer.
5. **Measure first** – BenchmarkDotNet + MemoryDiagnoser, then dotnet-counters under real load, then bombardier/k6 for the 60 k / 5 s test looking at p99.

## Next concrete steps (priority order)

1. Absolute offsets for parameter ranges (currently only length is stored).
2. Method trie or perfect-hash for methods instead of the if-chain (micro-optimisation).
3. Thin Kestrel `RequestDelegate` endpoint that only calls `TryMatch` (no middleware).
4. Optional YARP integration or a minimal forwarder with pooled `SocketsHttpHandler`.
5. Full 60 k-in-5 s load test with p99 reporting.
