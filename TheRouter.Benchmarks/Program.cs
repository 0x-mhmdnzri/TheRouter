using BenchmarkDotNet.Running;
using TheRouter.Benchmarks;

// Run the horrible HTTP matcher suite (the interesting one)
BenchmarkRunner.Run<HorribleHttpBenchmarks>();
