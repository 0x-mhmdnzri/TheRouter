# TheRouter

High-performance routing engine in C# — DAG graph + HTTP radix-trie matcher + streaming reverse-proxy.

## Quick start

```bash
dotnet run --project Web.API -c Release --urls http://127.0.0.1:5080
```

- **Swagger UI:** http://127.0.0.1:5080/swagger  
- **OpenAPI JSON:** http://127.0.0.1:5080/openapi/v1.json  

## Load-test result (in-process matcher)

```
POST /load-test  { "totalRequests": 60000, "concurrency": 64 }

→ ~442 000 RPS
→ p50 ≈ 0.001 ms | p99 ≈ 0.003 ms
→ 60 000 / 60 000 success
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
| Built-in 60k load probe | ✅ |

See [BACKLOG.md](BACKLOG.md) for full history and remaining optional items.
