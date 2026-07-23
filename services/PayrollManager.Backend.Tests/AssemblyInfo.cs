using Xunit;

// These tests point the process-global DbPaths.DataDirectoryOverride at a per-test temp
// directory. xUnit runs test classes in parallel by default, so two classes would clobber
// each other's override and read the wrong database. Serialize the assembly - the tests are
// fast SQLite operations, so the lost parallelism is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
