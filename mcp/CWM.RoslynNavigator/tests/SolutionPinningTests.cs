using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// A server started with a solution keeps it: MCP roots discovery never replaces it, whether a tool
/// call arrives while the load runs or after it failed. Roots are only for a server started without one.
/// </summary>
public sealed class SolutionPinningTests : IDisposable
{
    private readonly string _otherRoot;
    private int _rootsRequests;

    public SolutionPinningTests()
    {
        TestSolutionFixture.RegisterMSBuild();

        // A root that holds a different solution, which a pinned server must never load.
        _otherRoot = Path.Combine(Path.GetTempPath(), $"SolutionPinningTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_otherRoot);
        File.WriteAllText(Path.Combine(_otherRoot, "Other.sln"), "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_otherRoot))
            Directory.Delete(_otherRoot, recursive: true);
    }

    private WorkspaceManager CreateManager(bool configured, string rootDirectory)
    {
        var manager = new WorkspaceManager(
            NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(),
            TimeProvider.System)
        {
            HasConfiguredSolutions = configured,
            RootsProvider = _ =>
            {
                Interlocked.Increment(ref _rootsRequests);
                return Task.FromResult<IReadOnlyList<string>>([rootDirectory]);
            },
        };
        return manager;
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_ConfiguredSolutionStillLoading_KeepsConfiguredSolution()
    {
        var ct = TestContext.Current.CancellationToken;
        using var manager = CreateManager(configured: true, _otherRoot);

        var load = manager.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);
        var status = await manager.EnsureReadyOrStatusAsync(ct);
        await load;

        Assert.NotNull(status);
        Assert.Equal(0, _rootsRequests);
        Assert.Equal(1, manager.LoadCount);
        Assert.Equal(Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath), manager.SolutionDirectory);
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_ConfiguredSolutionMissing_ReportsPathAndNeverUsesRoots()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = Path.Combine(_otherRoot, "missing", "Gone.slnx");
        using var manager = CreateManager(configured: true, _otherRoot);

        await Assert.ThrowsAsync<FileNotFoundException>(() => manager.LoadSolutionAsync(missing, ct));
        var status = await manager.EnsureReadyOrStatusAsync(ct);

        Assert.Equal(WorkspaceState.Error, manager.State);
        Assert.Contains("Solution file not found", status);
        Assert.Equal(0, _rootsRequests);
        Assert.Equal(Path.GetDirectoryName(missing), manager.SolutionDirectory);
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_NoSolutionCallDuringLoad_DoesNotUseRoots()
    {
        var ct = TestContext.Current.CancellationToken;
        using var manager = CreateManager(configured: false, _otherRoot);

        var load = manager.LoadSolutionAsync(TestSolutionFixture.SampleSolutionPath, ct);
        await manager.EnsureReadyOrStatusAsync(ct);
        await load;

        Assert.Equal(0, _rootsRequests);
        Assert.Equal(1, manager.LoadCount);
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_NoSolution_LoadsSolutionFromRoots()
    {
        var ct = TestContext.Current.CancellationToken;
        var sampleRoot = Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!;
        using var manager = CreateManager(configured: false, sampleRoot);

        var status = await manager.EnsureReadyOrStatusAsync(ct);

        Assert.Null(status);
        Assert.Equal(1, _rootsRequests);
        Assert.Equal(WorkspaceState.Ready, manager.State);
    }
}
