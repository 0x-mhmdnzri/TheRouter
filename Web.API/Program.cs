using System.Text.Json.Serialization;
using TheRouter.Core.Graph;
using TheRouter.Core.Graph.Selectors;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();

// ── Build a sample DAG once at startup (immutable afterwards) ───────
var sampleGraph = new RouterGraphBuilder()
    .WithStartNode(1)
    .AddEdge(1, 2, weight: 10)
    .AddEdge(1, 3, weight: 5, conditionId: 1)
    .AddEdge(2, 4, weight: 1)
    .AddEdge(3, 4, weight: 2)
    .AddEdge(4, 5)
    .Build();

builder.Services.AddSingleton(sampleGraph);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var routeApi = app.MapGroup("/route");

// GET /route?strategy=first|lowest|highest
routeApi.MapGet("/", (
    RouterGraph graph,
    string? strategy) =>
{
    EdgeSelector selector = strategy?.ToLowerInvariant() switch
    {
        "lowest"  => BuiltInSelectors.LowestWeight,
        "highest" => BuiltInSelectors.HighestWeight,
        _         => BuiltInSelectors.First
    };

    Span<int> pathBuffer = stackalloc int[32];
    if (!graph.TryRoute(pathBuffer, out int len, selector))
        return Results.NotFound(new { error = "No path found" });

    var nodeIds = new int[len];
    for (int i = 0; i < len; i++)
        nodeIds[i] = graph.GetNode(pathBuffer[i]).Id;

    return Results.Ok(new RouteResponse(nodeIds, strategy ?? "first"));
})
.WithName("GetRoute");

// POST /route/all  – fan-out
routeApi.MapPost("/all", (RouterGraph graph) =>
{
    Span<int> flat = stackalloc int[128];
    if (!graph.TryRouteAll(flat, out int written, out int pathCount))
        return Results.BadRequest(new { error = "Buffer too small or no paths" });

    var paths = new List<int[]>(pathCount);
    int cursor = 0;
    for (int p = 0; p < pathCount; p++)
    {
        int plen = flat[cursor++];
        var ids = new int[plen];
        for (int i = 0; i < plen; i++)
            ids[i] = graph.GetNode(flat[cursor++]).Id;
        paths.Add(ids);
    }

    return Results.Ok(new FanOutResponse(paths));
})
.WithName("GetAllRoutes");

app.Run();

public sealed record RouteResponse(int[] Path, string Strategy);
public sealed record FanOutResponse(List<int[]> Paths);

[JsonSerializable(typeof(RouteResponse))]
[JsonSerializable(typeof(FanOutResponse))]
[JsonSerializable(typeof(int[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
