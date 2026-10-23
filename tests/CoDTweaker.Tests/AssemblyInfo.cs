using Xunit;

// The tests share static switches (app data root, backup root, fake game process), so they run one class at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
