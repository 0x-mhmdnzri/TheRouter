using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using TheRouter.Core.Http;
using Web.API.Proxy;

var builder = WebApplication.CreateSlimBuilder(args);

ThreadPool.SetMinThreads(workerThreads: 512, completionPortThreads: 512);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.AllowSynchronousIO = false;
    options.Limits.MaxConcurrentConnections = 20_000;
    options.Limits.MaxConcurrentUpgradedConnections = 20_000;
    options.Limits.MaxRequestBodySize = 64 * 1024;
    options.Limits.MinRequestBodyDataRate = null;
    options.Limits.MinResponseDataRate = null;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.Http2.MaxStreamsPerConnection = 1024;
    options.ConfigureEndpointDefaults(lo =>
    {
        // HTTP/1.1 is often lower latency for tiny responses under high concurrency
        lo.Protocols = HttpProtocols.Http1;
    });
});

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Error);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();

// ── Matcher (immutable after Freeze) ────────────────────────────────
var matcher = new RadixTrieMatcher();
string[] resources = ["users", "orders", "products", "invoices", "shipments", "payments"];
foreach (var r in resources)
{
    matcher.Map("GET", $"/api/v1/{r}", new MatchedRoute
    {
        Template = $"/api/v1/{r}", Method = "GET", HandlerId = 1,
        UpstreamBaseAddress = "http://127.0.0.1:9999",
        UpstreamPathTemplate = $"/upstream/{r}"
    });
    matcher.Map("POST", $"/api/v1/{r}", new MatchedRoute
    {
        Template = $"/api/v1/{r}", Method = "POST", HandlerId = 2,
        UpstreamBaseAddress = "http://127.0.0.1:9999",
        UpstreamPathTemplate = $"/upstream/{r}"
    });
    matcher.Map("GET", $"/api/v1/{r}/{{id}}", new MatchedRoute
    {
        Template = $"/api/v1/{r}/{{id}}", Method = "GET", HandlerId = 3,
        UpstreamBaseAddress = "http://127.0.0.1:9999",
        UpstreamPathTemplate = $"/upstream/{r}/by-id"
    });
    matcher.Map("GET", $"/api/v1/{r}/{{id}}/items/{{itemId}}", new MatchedRoute
    {
        Template = $"/api/v1/{r}/{{id}}/items/{{itemId}}", Method = "GET", HandlerId = 4
    });
}
matcher.Map("GET", "/health", new MatchedRoute { Template = "/health", Method = "GET", HandlerId = 0 });
matcher.Map("GET", "/route-info", new MatchedRoute { Template = "/route-info", Method = "GET", HandlerId = 10 });
matcher.Freeze();

builder.Services.AddSingleton(matcher);
builder.Services.AddSingleton(_ => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 200,
    EnableMultipleHttp2Connections = true,
    ConnectTimeout = TimeSpan.FromSeconds(5)
});
builder.Services.AddSingleton(sp =>
    new HttpMessageInvoker(sp.GetRequiredService<SocketsHttpHandler>(), disposeHandler: false));
builder.Services.AddSingleton<ReverseProxy>();

var app = builder.Build();

// Precomputed responses
ReadOnlyMemory<byte> healthMem = Encoding.UTF8.GetBytes("""{"status":"ok"}""");
ReadOnlyMemory<byte> okMem = Encoding.UTF8.GetBytes("""{"ok":true}""");
ReadOnlyMemory<byte> routeInfoMem = Encoding.UTF8.GetBytes("""{"t":"/api/v1/users/{id}/items/{itemId}","h":4,"p":[42,7]}""");
ReadOnlyMemory<byte> notFoundMem = Encoding.UTF8.GetBytes("""{"error":"not found"}""");

// ═══════════════════════════════════════════════════════════════════
// FAST PATH — middleware before endpoint routing / DI
// Handles /route-info, /health, /bench/* with zero DI resolution.
// ═══════════════════════════════════════════════════════════════════
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value; // already interned/cached per request by Kestrel
    if (path is null)
    {
        await next();
        return;
    }

    // /health
    if (path.Length == 7 && path.equals_health())
    {
        await WriteFastAsync(ctx, 200, healthMem);
        return;
    }

    // /route-info  (live match + fixed body)
    if (path.Length == 11 && path.equals_route_info())
    {
        Span<(int Start, int Length)> ranges = stackalloc (int, int)[8];
        // Constant sample path as ReadOnlySpan — no string alloc
        if (!matcher.TryMatch("GET", "/api/v1/users/42/items/7", out var ep, ranges, out _) || ep is null)
        {
            await WriteFastAsync(ctx, 404, notFoundMem);
            return;
        }
        await WriteFastAsync(ctx, 200, routeInfoMem);
        return;
    }

    // /bench/...
    if (path.StartsWith("/bench", StringComparison.Ordinal))
    {
        var inner = path.Length > 6 ? path.AsSpan(6) : "/".AsSpan();
        if (inner.Length == 0) inner = "/";
        Span<(int, int)> ranges = stackalloc (int, int)[8];
        if (!matcher.TryMatch("GET", inner, out var ep, ranges, out _) || ep is null)
        {
            await WriteFastAsync(ctx, 404, notFoundMem);
            return;
        }
        await WriteFastAsync(ctx, 200, okMem);
        return;
    }

    await next();
});

app.MapOpenApi();
app.MapGet("/swagger", () => Results.Content("""
<!DOCTYPE html>
<html><head><title>TheRouter Swagger</title>
<link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head>
<body><div id="swagger-ui"></div>
<script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
<script>SwaggerUIBundle({url:'/openapi/v1.json',dom_id:'#swagger-ui'});</script>
</body></html>
""", "text/html")).ExcludeFromDescription();

// Keep named endpoints for OpenAPI discovery (less hot path)
app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("Health").WithTags("System").ExcludeFromDescription();

app.MapGet("/route-info", () => Results.Bytes(routeInfoMem.ToArray(), "application/json"))
    .WithName("RouteInfo").WithTags("Matcher");

app.MapMethods("/api/{**catchAll}",
    ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"],
    async (HttpContext ctx, RadixTrieMatcher m, ReverseProxy proxy) =>
{
    var method = ctx.Request.Method.AsSpan();
    var path = ctx.Request.Path.Value.AsSpan();
    Span<(int Start, int Length)> ranges = stackalloc (int, int)[8];
    if (!m.TryMatch(method, path, out var endpoint, ranges, out int paramCount))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse("No matching route"), AppJsonSerializerContext.Default.ErrorResponse);
        return;
    }
    if (!string.IsNullOrEmpty(endpoint!.UpstreamBaseAddress))
    {
        await proxy.ForwardAsync(ctx, endpoint, ctx.RequestAborted).ConfigureAwait(false);
        return;
    }
    var pathStr = ctx.Request.Path.Value ?? "";
    var bound = new List<string>(paramCount);
    for (int i = 0; i < paramCount; i++)
        bound.Add(ParamBinder.AsSpan(pathStr.AsSpan(), ranges[i]).ToString());
    await ctx.Response.WriteAsJsonAsync(
        new LocalRouteResponse(endpoint.Template, endpoint.HandlerId, bound),
        AppJsonSerializerContext.Default.LocalRouteResponse);
}).WithName("ApiRouter").WithTags("Router");

app.MapPost("/load-test", async (LoadTestRequest req, RadixTrieMatcher m) =>
{
    int total = req.TotalRequests <= 0 ? 60_000 : req.TotalRequests;
    int concurrency = req.Concurrency <= 0 ? 64 : req.Concurrency;
    long success = 0;
    var latencies = new ConcurrentBag<long>();
    var sw = Stopwatch.StartNew();
    await Parallel.ForEachAsync(Enumerable.Range(0, total),
        new ParallelOptions { MaxDegreeOfParallelism = concurrency },
        (i, ct) =>
        {
            var path = i % 3 == 0 ? "/api/v1/users/42/items/7"
                     : i % 3 == 1 ? "/api/v1/orders" : "/api/v1/products/15";
            var t0 = Stopwatch.GetTimestamp();
            Span<(int, int)> ranges = stackalloc (int, int)[8];
            if (m.TryMatch("GET", path, out _, ranges, out _)) Interlocked.Increment(ref success);
            latencies.Add(Stopwatch.GetTimestamp() - t0);
            return ValueTask.CompletedTask;
        });
    sw.Stop();
    var sorted = latencies.Select(t => t / (double)Stopwatch.Frequency * 1000.0).OrderBy(x => x).ToArray();
    double P(double pct) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(pct / 100.0 * sorted.Length))];
    return Results.Ok(new LoadTestResult(total, success, sw.Elapsed.TotalSeconds, total / sw.Elapsed.TotalSeconds,
        P(50), P(99), P(99.9), sorted.Length > 0 ? sorted[^1] : 0));
}).WithName("LoadTest").WithTags("LoadTest");

app.Run();

// ── Helpers ─────────────────────────────────────────────────────────
static ValueTask<System.IO.Pipelines.FlushResult> WriteFastAsync(HttpContext ctx, int status, ReadOnlyMemory<byte> body)
{
    var res = ctx.Response;
    res.StatusCode = status;
    res.ContentType = "application/json";
    res.ContentLength = body.Length;
    // BodyWriter avoids some Stream overhead vs Body.WriteAsync
    return res.BodyWriter.WriteAsync(body, ctx.RequestAborted);
}

internal static class PathFastEquals
{
    // Avoid culture-aware compare; JIT can inline these
    public static bool equals_health(this string path)
        => path[1] == 'h' && path[2] == 'e' && path[3] == 'a' && path[4] == 'l' && path[5] == 't' && path[6] == 'h';

    public static bool equals_route_info(this string path)
        => path[1] == 'r' && path[2] == 'o' && path[3] == 'u' && path[4] == 't' && path[5] == 'e'
        && path[6] == '-' && path[7] == 'i' && path[8] == 'n' && path[9] == 'f' && path[10] == 'o';
}

public sealed record HealthResponse(string Status, DateTimeOffset At);
public sealed record ErrorResponse(string Error);
public sealed record LocalRouteResponse(string Template, int HandlerId, List<string> Params);
public sealed record LoadTestRequest(int TotalRequests = 60_000, int Concurrency = 64);
public sealed record LoadTestResult(
    long Total, long Success, double ElapsedSec, double Rps,
    double P50Ms, double P99Ms, double P999Ms, double MaxMs);

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(LocalRouteResponse))]
[JsonSerializable(typeof(LoadTestRequest))]
[JsonSerializable(typeof(LoadTestResult))]
[JsonSerializable(typeof(List<string>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
