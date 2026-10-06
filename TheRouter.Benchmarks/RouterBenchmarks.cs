using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using TheRouter.Core.Graph;
using TheRouter.Core.Graph.Selectors;

namespace TheRouter.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class RouterBenchmarks
{
    private RouterGraph _graph = null!;
    private int[] _pathBuffer = null!;
    private int[] _dist = null!;
    private int[] _prev = null!;
    private int[] _heap = null!;
    private int[] _flat = null!;

    [GlobalSetup]
    public void Setup()
    {
        // A slightly larger DAG for more realistic numbers
        var b = new RouterGraphBuilder().WithStartNode(0);
        for (int i = 0; i < 50; i++)
        {
            b.AddEdge(i, i + 1, weight: 1 + (i % 5));
            if (i % 3 == 0 && i + 2 <= 50)
                b.AddEdge(i, i + 2, weight: 3 + (i % 3));
        }
        _graph = b.Build();

        _pathBuffer = new int[64];
        _dist = new int[_graph.NodeCount];
        _prev = new int[_graph.NodeCount];
        _heap = new int[_graph.NodeCount];
        _flat = new int[512];
    }

    [Benchmark(Baseline = true)]
    public bool TryRoute_First()
    {
        return _graph.TryRoute(_pathBuffer, out _, BuiltInSelectors.First);
    }

    [Benchmark]
    public bool TryRoute_LowestWeight()
    {
        return _graph.TryRoute(_pathBuffer, out _, BuiltInSelectors.LowestWeight);
    }

    [Benchmark]
    public bool TryRouteAll_FanOut()
    {
        return _graph.TryRouteAll(_flat, out _, out _);
    }

    [Benchmark]
    public bool Dijkstra_ToLastNode()
    {
        return ShortestPath.TryDijkstra(
            _graph,
            startIndex: 0,
            targetIndex: _graph.NodeCount - 1,
            _dist, _prev, _heap, _pathBuffer,
            out _, out _);
    }

    [Benchmark]
    public bool Dijkstra_ToAnySink()
    {
        return ShortestPath.TryShortestPathToAnySink(
            _graph, _dist, _prev, _heap, _pathBuffer,
            out _, out _);
    }
}
