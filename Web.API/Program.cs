using System.Text.Json.Serialization;
using TheRouter.Core.Http;
using RouteEndpoint = TheRouter.Core.Http.RouteEndpoint;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// ── Build & freeze the matcher once at startup ──────────────────────
var matcher = new RadixTrieMatcher();

string[] resources = ["users", "orders", "products", "invoices", "shipments", "payments"];
foreach (var r in resources)
{
    matcher.Map("GET",  $"/api/v1/{r}", new RouteEndpoint { Template = $"/api/v1/{r}", Method = "GET", HandlerId = 1 });
    matcher.Map("POST", $"/api/v1/{r}", new RouteEndpoint { Template = $"/api/v1/{r}", Method = "POST", HandlerId = 2 });
    matcher.Map("GET",  $"/api/v1/{r}/{{id}}", new RouteEndpoint { Template = $"/api/v1/{r}/{{id}}", Method = "GET", HandlerId = 3 });
    matcher.Map("GET",  $"/api/v1/{r}/{{id}}/items/{{itemId}}", new RouteEndpoint { Template = $"/api/v1/{r}/{{id}}/items/{{itemId}}", Method = "GET", HandlerId = 4 });
}
matcher.Map("GET", "/health", new RouteEndpoint { Template = "/health", Method = "GET", HandlerId = 0 });
matcher.Freeze();

builder.Services.AddSingleton(matcher);

// Shared handler for future proxy work
builder.Services.AddSingleton(_ => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 100,
    EnableMultipleHttp2Connections = true
});
builder.Services.AddSingleton(sp => new HttpMessageInvoker(sp.GetRequiredService<SocketsHttpHandler>(), disposeHandler: false));

var app = builder.Build();

// ── Ultra-thin matching endpoint (no middleware pipeline beyond this) ─
app.MapMethods("{**path}", ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"], async (
    HttpContext ctx,
    RadixTrieMatcher matcher) =>
{
    var method = ctx.Request.Method.AsSpan();
    var path = ctx.Request.Path.Value.AsSpan();

    Span<(int Start, int Length)> ranges = stackalloc (int, int)[8];

    if (!matcher.TryMatch(method, path, out var endpoint, ranges, out int paramCount))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    // Extremely light response – prove matching cost, not serialization cost
    ctx.Response.StatusCode = 200;
    ctx.Response.ContentType = "application/json";

    // Manual tiny JSON to avoid JsonSerializer allocation on the hot path for this test
    await ctx.Response.WriteAsync(
        $"{{\"template\":\"{endpoint!.Template}\",\"handler\":{endpoint.HandlerId},\"params\":{paramCount}}}",
        ctx.RequestAborted).ConfigureAwait(false);
});

// ── Minimal proxy example (forward to a fixed upstream for now) ─────
// This is the starting point for the real forward layer.
app.MapPost("/proxy/{**catchAll}", async (
    HttpContext ctx,
    HttpMessageInvoker invoker) =>
{
    // Very basic forward – body streamed, no buffering
    var upstream = new Uri("http://127.0.0.1:9999/"); // placeholder
    using var req = new HttpRequestMessage(HttpMethod.Post, upstream)
    {
        Content = new StreamContent(ctx.Request.Body)
    };

    foreach (var h in ctx.Request.Headers)
    {
        if (!req.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray()))
            req.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
    }

    using var resp = await invoker.SendAsync(req, ctx.RequestAborted).ConfigureAwait(false);
    ctx.Response.StatusCode = (int)resp.StatusCode;
    foreach (var h in resp.Headers)
        ctx.Response.Headers[h.Key] = h.Value.ToArray();
    if (resp.Content is not null)
        await resp.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);
});

app.Run();

[JsonSerializable(typeof(object))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
