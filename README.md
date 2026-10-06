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

### Measured — warm-up 5s (Windows, `-c 500`)

```
oha -c 500 -z 5s --no-tui http://localhost:5014/route-info

Success rate: 100.00%
Requests/sec: 102 064
Average:      4.9 ms
Fastest:      0.1 ms
Slowest:      1.37 s
Size/request: 58 B

Percentiles:
  p10  0.6 ms
  p25  1.0 ms
  p50  1.9 ms
  p75  5.9 ms
  p90  7.0 ms
  p95  8.8 ms
  p99  39.4 ms
  p99.9 267 ms
  p99.99 1.11 s

Responses: 513 090 × 200
```

### Measured — sustained 60s (earlier run, pre–ThreadPool bump)

```
Requests/sec: 55 825
Average:      8.95 ms
p50 5.45 ms | p99 30.8 ms
```

Progression:

| Stage | RPS | Average | Notes |
|-------|-----|---------|--------|
| Before Kestrel/logging opts | ~16.7k | ~30 ms | List + JsonSerializer on hot path |
| After slim + static bytes | ~55.8k | ~9 ms | Live `TryMatch` + precomputed body |
| After ThreadPool + compact JSON | **~102k** | **~4.9 ms** | 5s warm-up run, 58 B/response |

In-process matcher alone (no HTTP): **~400k+ matches/sec**, ~0 B/op.

Remaining tail (p99.9+) is mostly connection setup / scheduling under `-c 500`, not match cost (see DNS+dialup ~300 ms in details).

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
