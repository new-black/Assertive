using Xunit;

// GeneratedAssert.InterceptedCallCount is process-global; tests assert on its deltas, so
// classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
