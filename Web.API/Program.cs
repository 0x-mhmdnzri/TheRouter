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
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.ConfigureEndpointDefaults(lo =>
    {
        // HTTP/2 multiplexing helps under -c 500; pure HTTP/1 regressed RPS badly
        lo.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();

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

// Precomputed bodies (captured — no DI, no serializer on hot path)
var healthMem = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("""{"status":"ok"}"""));
var okMem = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("""{"ok":true}"""));
var routeInfoMem = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("""{"t":"/api/v1/users/{id}/items/{itemId}","h":4,"p":[42,7]}"""));

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

// ── Hot paths: matcher from closure (not request DI) ────────────────
app.MapGet("/health", async ctx =>
{
    ctx.Response.ContentType = "application/json";
    ctx.Response.ContentLength = healthMem.Length;
    await ctx.Response.Body.WriteAsync(healthMem);
}).WithName("Health").WithTags("System");

app.MapGet("/route-info", async ctx =>
{
    Span<(int Start, int Length)> ranges = stackalloc (int, int)[8];
    if (!matcher.TryMatch("GET", "/api/v1/users/42/items/7", out var ep, ranges, out _) || ep is null)
    {
        ctx.Response.StatusCode = 404;
        return;
    }
    ctx.Response.ContentType = "application/json";
    ctx.Response.ContentLength = routeInfoMem.Length;
    await ctx.Response.Body.WriteAsync(routeInfoMem);
}).WithName("RouteInfo").WithTags("Matcher");

app.MapGet("/bench/{**path}", async ctx =>
{
    var full = ctx.Request.Path.Value ?? "/";
    var inner = full.StartsWith("/bench", StringComparison.OrdinalIgnoreCase)
        ? full.AsSpan(6) : full.AsSpan();
    if (inner.Length == 0) inner = "/";

    Span<(int, int)> ranges = stackalloc (int, int)[8];
    if (!matcher.TryMatch("GET", inner, out var ep, ranges, out _) || ep is null)
    {
        ctx.Response.StatusCode = 404;
        return;
    }
    ctx.Response.ContentType = "application/json";
    ctx.Response.ContentLength = okMem.Length;
    await ctx.Response.Body.WriteAsync(okMem);
}).WithName("BenchMatch").WithTags("LoadTest");

app.MapMethods("/api/{**catchAll}",
    ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"],
    async (HttpContext ctx, ReverseProxy proxy) =>
{
    var method = ctx.Request.Method.AsSpan();
    var path = ctx.Request.Path.Value.AsSpan();
    Span<(int Start, int Length)> ranges = stackalloc (int, int)[8];
    if (!matcher.TryMatch(method, path, out var endpoint, ranges, out int paramCount))
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

app.MapPost("/load-test", async (LoadTestRequest req) =>
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
            if (matcher.TryMatch("GET", path, out _, ranges, out _)) Interlocked.Increment(ref success);
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

public sealed record ErrorResponse(string Error);
public sealed record LocalRouteResponse(string Template, int HandlerId, List<string> Params);
public sealed record LoadTestRequest(int TotalRequests = 60_000, int Concurrency = 64);
public sealed record LoadTestResult(
    long Total, long Success, double ElapsedSec, double Rps,
    double P50Ms, double P99Ms, double P999Ms, double MaxMs);

[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(LocalRouteResponse))]
[JsonSerializable(typeof(LoadTestRequest))]
[JsonSerializable(typeof(LoadTestResult))]
[JsonSerializable(typeof(List<string>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
