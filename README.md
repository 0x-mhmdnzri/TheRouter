# TheRouter

High-performance routing engine in C# — DAG graph + HTTP radix-trie matcher + streaming reverse-proxy.

## Quick start

```bash
dotnet run --project Web.API -c Release --launch-profile http
```

| URL | Purpose |
|-----|---------|
| http://localhost:5014/swagger | Swagger UI |
| http://localhost:5014/openapi/v1.json | OpenAPI |
| http://localhost:5014/route-info | Live match + compact JSON (load-test target) |
| http://localhost:5014/bench/api/v1/users/42/items/7 | Ultra-thin match |
| http://localhost:5014/health | Static health |

## Load test results (`oha`)

### Command

```powershell
oha -c 500 -z 60s --latency-correction --no-tui http://localhost:5014/route-info
```

### Measured (Windows, 500 concurrent, 60s)

```
Success rate: 100.00%
Total:        60.0 s
Requests/sec: 55 825
Average:      8.95 ms
Fastest:      0.065 ms
Slowest:      887 ms

Percentiles:
  p10  2.08 ms
  p25  3.36 ms
  p50  5.45 ms
  p75  14.87 ms
  p90  19.66 ms
  p95  22.46 ms
  p99  30.81 ms
  p99.9 43.23 ms
  p99.99 173 ms

Total responses: 3 350 562 × 200
Size/request:    167 B  (older build; current compact payload is ~80 B)
```

Progression:

| Stage | RPS | Average | Notes |
|-------|-----|---------|--------|
| Before Kestrel/logging opts | ~16.7k | ~30 ms | List + JsonSerializer on hot path |
| After slim + static bytes | **~55.8k** | **~9 ms** | Live `TryMatch` + precomputed body |

In-process matcher alone (no HTTP): **~400k+ matches/sec**, ~0 B/op.

## How to push further

1. **Client side (`oha`)**  
   - Warm-up: `oha -c 500 -z 5s ...` then the 60s run (cuts DNS+dialup noise).  
   - Prefer HTTP/2 if available: `oha --http2 ...`  
   - DNS+dialup ~300 ms in the report is mostly **new connection** cost under `-c 500`, not match latency.

2. **Server** (already applied in current code)  
   - `CreateSlimBuilder`, logging = Warning  
   - Kestrel: high concurrent connection limits, long KeepAlive  
   - `ThreadPool.SetMinThreads(512, 512)`  
   - Server GC + Tiered PGO  
   - Compact response body for `/route-info`  
   - `ReadOnlyMemory<byte>` writes (no extra copies)

3. **Next gains (if needed)**  
   - Benchmark `/health` and `/bench/...` separately (isolates match vs JSON size).  
   - Pin process / use fewer, larger machines (tail latency often = scheduling).  
   - HTTP/2 only endpoint + client.  
   - For reverse-proxy paths: upstream latency dominates; tune `SocketsHttpHandler` pool.

```powershell
# Isolate layers
oha -c 500 -z 30s --no-tui http://localhost:5014/health
oha -c 500 -z 30s --no-tui http://localhost:5014/bench/api/v1/users/42/items/7
oha -c 500 -z 30s --no-tui http://localhost:5014/route-info
```

## Features

| Area | Status |
|------|--------|
| CSR DAG + zero-alloc traversal | ✅ |
| Weighted / conditional selectors | ✅ |
| Fan-out, Dijkstra, Topological DP | ✅ |
| Hot-swappable + versioned graphs | ✅ |
| HTTP radix-trie + Span matching | ✅ |
| Absolute param ranges + ParamBinder | ✅ |
| Streaming reverse-proxy | ✅ |
| OpenAPI + Swagger UI | ✅ |
| High-concurrency Kestrel tuning | ✅ |

See [BACKLOG.md](BACKLOG.md) for history and optional items.
