# TheRouter Backlog

Last updated: 2026-10-06

## Done

- [x] DAG CSR graph engine (zero-alloc traversal)
- [x] Weighted / conditional edge selectors
- [x] Fan-out (`TryRouteAll`)
- [x] Dijkstra with caller-supplied buffers
- [x] HotSwappableGraph (lock-free double buffering)
- [x] BenchmarkDotNet suite (graph)
- [x] HTTP Radix-Trie matcher + `AlternateLookup<ReadOnlySpan<char>>`
- [x] Zero-alloc method lookup (GET/POST/…)
- [x] Horrible allocation test case (exposed method.ToString bug → fixed to ~0 B/op)
- [x] Absolute parameter ranges `(start, length)` into original path
- [x] Thin Kestrel `RequestDelegate`-style endpoint (no heavy middleware)
- [x] Shared `SocketsHttpHandler` + basic streaming proxy skeleton

## In Progress / Next (priority order)

1. **Absolute parameter ranges** – DONE (this commit)
2. **Thin Kestrel endpoint** – DONE (this commit)
3. **Load test ≈ 60 k requests in 5 s** – measure real RPS + p99 on the thin endpoint
4. **Forward / Proxy layer**
   - Proper upstream selection from matched endpoint
   - Header filtering / hop-by-hop removal
   - Timeout + cancellation propagation
   - Connection pooling tuning under real load
5. **Parameter binding helpers** – turn ranges into typed values without string alloc when possible
6. **Method trie / perfect hash** (micro-opt, low priority)
7. **Topological DP shortest-path** specialized for pure DAGs (graph side)
8. **Graph versioning / snapshots** beyond simple hot-swap
9. **YARP integration option** (if owning the full proxy becomes too costly)

## Measurement checklist for the 60 k / 5 s test

- [ ] bombardier / k6 / NBomber against the thin endpoint
- [ ] Report: total requests, RPS, p50 / p99 / p999 latency
- [ ] `dotnet-counters` : allocation-rate, gen-0-gc-count, gen-2-gc-count
- [ ] Confirm matching path still shows ~0 B/op under concurrent load

## Design invariants (do not break)

- Route table is immutable after `Freeze()` / startup
- Hot path of `TryMatch` must stay allocation-free for common methods
- Readers of `HotSwappableGraph` never take locks
- Proxy must stream; never buffer the whole body
