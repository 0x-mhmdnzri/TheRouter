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

### Best sustained run — 60s (`-c 500`)

```
oha -c 500 -z 60s --latency-correction --no-tui http://localhost:5014/route-info

Success rate: 100.00%
Requests/sec: 77 157
Average:      6.5 ms
Fastest:      0.1 ms
Slowest:      1.06 s
Size/request: 58 B
Total 200s:   4 631 058

Percentiles:
  p10  2.1 ms
  p25  5.7 ms
  p50  6.2 ms
  p75  7.2 ms
  p90  8.5 ms
  p95  10.8 ms
  p99  16.6 ms
  p99.9 48 ms
  p99.99 228 ms
```

### Warm-up 5s peak (same endpoint)

```
Requests/sec: ~102 000
Average:      ~4.9 ms
p50:          ~1.9 ms
```

Progression:

| Stage | RPS | Average | p99 | Notes |
|-------|-----|---------|-----|--------|
| Initial (JsonSerializer + List) | ~16.7k | ~30 ms | ~71 ms | allocations on hot path |
| Slim + static bytes | ~55.8k | ~9 ms | ~31 ms | precomputed body |
| ThreadPool + compact JSON | ~102k (5s) | ~4.9 ms | ~39 ms | peak warm-up |
| Sustained 60s (HTTP/1+2, closure) | **~77k** | **~6.5 ms** | **~17 ms** | production-like |

In-process matcher alone (no HTTP): **~400k+ matches/sec**, ~0 B/op.

Tail beyond p99 is dominated by connection setup / OS scheduling (DNS+dialup avg ~300 ms on new connections), not `TryMatch`.

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
