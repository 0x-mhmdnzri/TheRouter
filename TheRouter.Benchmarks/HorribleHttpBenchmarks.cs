using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using TheRouter.Core.Http;

namespace TheRouter.Benchmarks;

/// <summary>
/// "Horrible" test case designed to stress the matcher:
/// - 1 200+ routes with mixed static / parameter / deep paths
/// - Hot paths (same URL repeated) vs cold paths (unique every time)
/// - Deep nesting and many parameters
/// Goal: surface any hidden allocations and measure real matching cost.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class HorribleHttpBenchmarks
{
    private RadixTrieMatcher _matcher = null!;
    private string[] _hotPaths = null!;
    private string[] _coldPaths = null!;
    private int _hotIndex;
    private int _coldIndex;

    [GlobalSetup]
    public void Setup()
    {
        _matcher = new RadixTrieMatcher();

        // ── 1. Classic REST resources ───────────────────────────────
        string[] resources = ["users", "orders", "products", "invoices", "shipments", "payments", "reviews", "categories"];
        foreach (var r in resources)
        {
            _matcher.Map("GET",  $"/api/v1/{r}", new MatchedRoute { Template = $"/api/v1/{r}", Method = "GET", HandlerId = 1 });
            _matcher.Map("POST", $"/api/v1/{r}", new MatchedRoute { Template = $"/api/v1/{r}", Method = "POST", HandlerId = 2 });
            _matcher.Map("GET",  $"/api/v1/{r}/{{id}}", new MatchedRoute { Template = $"/api/v1/{r}/{{id}}", Method = "GET", HandlerId = 3 });
            _matcher.Map("PUT",  $"/api/v1/{r}/{{id}}", new MatchedRoute { Template = $"/api/v1/{r}/{{id}}", Method = "PUT", HandlerId = 4 });
            _matcher.Map("DELETE", $"/api/v1/{r}/{{id}}", new MatchedRoute { Template = $"/api/v1/{r}/{{id}}", Method = "DELETE", HandlerId = 5 });
            _matcher.Map("GET",  $"/api/v1/{r}/{{id}}/items", new MatchedRoute { Template = $"/api/v1/{r}/{{id}}/items", Method = "GET", HandlerId = 6 });
            _matcher.Map("GET",  $"/api/v1/{r}/{{id}}/items/{{itemId}}", new MatchedRoute { Template = $"/api/v1/{r}/{{id}}/items/{{itemId}}", Method = "GET", HandlerId = 7 });
            _matcher.Map("GET",  $"/api/v1/{r}/{{id}}/items/{{itemId}}/details", new MatchedRoute { Template = $"/api/v1/{r}/{{id}}/items/{{itemId}}/details", Method = "GET", HandlerId = 8 });
        }

        // ── 2. Deep nested admin paths ──────────────────────────────
        for (int i = 0; i < 50; i++)
        {
            _matcher.Map("GET", $"/admin/level1/level2/level3/level4/resource{i}", new MatchedRoute
            {
                Template = $"/admin/level1/level2/level3/level4/resource{i}",
                Method = "GET",
                HandlerId = 100 + i
            });
            _matcher.Map("GET", $"/admin/level1/level2/level3/level4/resource{i}/{{id}}/sub/{{subId}}", new MatchedRoute
            {
                Template = $"/admin/level1/level2/level3/level4/resource{i}/{{id}}/sub/{{subId}}",
                Method = "GET",
                HandlerId = 200 + i
            });
        }

        // ── 3. Catch-all style ──────────────────────────────────────
        _matcher.Map("GET", "/static/{*path}", new MatchedRoute { Template = "/static/{*path}", Method = "GET", HandlerId = 999 });
        _matcher.Map("GET", "/files/{*path}", new MatchedRoute { Template = "/files/{*path}", Method = "GET", HandlerId = 998 });

        // ── 4. Many near-identical static routes (hash pressure) ────
        for (int i = 0; i < 200; i++)
        {
            _matcher.Map("GET", $"/legacy/page-{i:D4}", new MatchedRoute
            {
                Template = $"/legacy/page-{i:D4}",
                Method = "GET",
                HandlerId = 1000 + i
            });
        }

        _matcher.Freeze();

        // Hot set – the same 20 paths repeated over and over
        _hotPaths =
        [
            "/api/v1/users",
            "/api/v1/users/42",
            "/api/v1/orders/99/items/7",
            "/api/v1/products/15/items/3/details",
            "/admin/level1/level2/level3/level4/resource12",
            "/admin/level1/level2/level3/level4/resource7/55/sub/88",
            "/static/css/main.css",
            "/legacy/page-0042",
            "/api/v1/invoices/1001",
            "/api/v1/payments",
            "/api/v1/users/1/items",
            "/api/v1/orders",
            "/api/v1/categories/5",
            "/files/docs/readme.md",
            "/api/v1/shipments/200/items/1/details",
            "/legacy/page-0001",
            "/api/v1/reviews/33",
            "/admin/level1/level2/level3/level4/resource0",
            "/api/v1/products",
            "/api/v1/users/999/items/1"
        ];

        // Cold set – unique paths every time (forces different trie walks)
        var cold = new List<string>(500);
        for (int i = 0; i < 100; i++)
        {
            cold.Add($"/api/v1/users/{i}");
            cold.Add($"/api/v1/orders/{i}/items/{i % 10}");
            cold.Add($"/admin/level1/level2/level3/level4/resource{i % 50}/{i}/sub/{i * 2}");
            cold.Add($"/legacy/page-{i:D4}");
            cold.Add($"/static/assets/file-{i}.js");
        }
        _coldPaths = cold.ToArray();
    }

    [Benchmark(Baseline = true)]
    public bool Hot_Path_Match()
    {
        var p = _hotPaths[_hotIndex++ % _hotPaths.Length];
        return _matcher.TryMatch("GET", p, out _);
    }

    [Benchmark]
    public bool Cold_Path_Match()
    {
        var p = _coldPaths[_coldIndex++ % _coldPaths.Length];
        return _matcher.TryMatch("GET", p, out _);
    }

    [Benchmark]
    public bool Hot_Path_With_Params()
    {
        var p = _hotPaths[2]; // /api/v1/orders/99/items/7
        Span<(int, int)> ranges = stackalloc (int, int)[4];
        return _matcher.TryMatch("GET", p, out _, ranges, out _);
    }

    [Benchmark]
    public bool Miss_Path()
    {
        return _matcher.TryMatch("GET", "/this/does/not/exist/at/all", out _);
    }
}
