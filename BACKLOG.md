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
- [x] Thin Kestrel endpoint (no heavy middleware)
- [x] Shared `SocketsHttpHandler` + streaming proxy
- [x] **Parameter binding helpers** (`ParamBinder` – span / int / long / guid without mandatory string)
- [x] **Forward / Proxy layer**
  - Upstream from `MatchedRoute.UpstreamBaseAddress`
  - Hop-by-hop header filtering
  - Timeout + cancellation propagation
  - Connection pooling (`PooledConnectionLifetime`, `MaxConnectionsPerServer`, HTTP/2)
- [x] **Load test 60k requests** (in-process matcher stress)
  - Result: **~442k RPS**, p50 ≈ 0.001 ms, p99 ≈ 0.003 ms, 60k/60k success
- [x] **Topological DP shortest-path** for pure DAGs (`TopologicalShortestPath`)
- [x] **Graph versioning** (`GraphVersionStore` – bounded history + lock-free current)
- [x] **OpenAPI + Swagger UI**
  - `/openapi/v1.json`
  - `/swagger` (Swagger UI via CDN)

## Remaining / optional

- [ ] Method trie / perfect hash (micro-opt, low priority – current if-chain is fine)
- [ ] Full external HTTP load test (bombardier/k6 against Kestrel, not in-process)
- [ ] YARP integration option (only if owning proxy becomes too costly)
- [ ] Parameter binding for more types / model binding integration

## Design invariants (do not break)

- Route table is immutable after `Freeze()` / startup
- Hot path of `TryMatch` must stay allocation-free for common methods
- Readers of `HotSwappableGraph` / `GraphVersionStore.Current` never take locks
- Proxy must stream; never buffer the whole body

## Key URLs

| Path | Purpose |
|------|---------|
| `/swagger` | Swagger UI |
| `/openapi/v1.json` | OpenAPI document |
| `/health` | Health check |
| `/route-info` | Demo match + absolute param ranges + typed bind |
| `/bench/{**path}` | Pure match bench endpoint |
| `/api/{**catchAll}` | Main router (local or proxy) |
| `POST /load-test` | In-process 60k match stress test |
