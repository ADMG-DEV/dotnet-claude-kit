using System.Text.Json.Nodes;
using CWM.RoslynNavigator.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace CWM.RoslynNavigator.Tests;

/// <summary>
/// <c>--solution-in-root</c> loads the named solution from the checkout the client's session is in,
/// and <c>--audit-dir</c> records each call with the files its answer came from.
/// </summary>
public sealed class ReviewEvidenceTests : IDisposable
{
    private readonly string _outside;
    private readonly string _auditDir;

    public ReviewEvidenceTests()
    {
        TestSolutionFixture.RegisterMSBuild();
        _outside = Path.Combine(Path.GetTempPath(), $"ReviewEvidenceTests_{Guid.NewGuid():N}");
        _auditDir = Path.Combine(_outside, "audit");
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outside))
            Directory.Delete(_outside, recursive: true);
    }

    private static string SampleRoot => Path.GetDirectoryName(TestSolutionFixture.SampleSolutionPath)!;

    private static WorkspaceManager CreateManager(string root, string? fallback = null) => new(
        NullLoggerFactory.Instance.CreateLogger<WorkspaceManager>(), TimeProvider.System)
    {
        HasConfiguredSolutions = false,
        RootSolutionName = "SampleSolution.sln",
        FallbackSolutionPath = fallback,
        RootsProvider = _ => Task.FromResult<IReadOnlyList<string>>([root]),
    };

    [Fact]
    public void FindNamedSolutionAtOrAbove_FromAFolderInsideTheCheckout_FindsTheCheckoutsSolution()
    {
        var found = SolutionDiscovery.FindNamedSolutionAtOrAbove(Path.Combine(SampleRoot, "SampleDomain"), "SampleSolution.sln");

        Assert.Equal(TestSolutionFixture.SampleSolutionPath, found, ignoreCase: true);
    }

    [Fact]
    public void FindNamedSolutionAtOrAbove_OutsideAnyCheckout_ReturnsNull()
    {
        Assert.Null(SolutionDiscovery.FindNamedSolutionAtOrAbove(_outside, $"NoSuch_{Guid.NewGuid():N}.slnx"));
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_SessionInsideTheCheckout_LoadsThatCheckout()
    {
        var ct = TestContext.Current.CancellationToken;
        using var manager = CreateManager(Path.Combine(SampleRoot, "SampleApi"));

        await manager.EnsureReadyOrStatusAsync(ct);

        Assert.Equal(WorkspaceState.Ready, manager.State);
        Assert.Equal([TestSolutionFixture.SampleSolutionPath], manager.LoadedSolutionPaths);
    }

    [Fact]
    public async Task EnsureReadyOrStatusAsync_SessionOutsideAnyCheckout_LoadsTheFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        using var manager = CreateManager(_outside, fallback: TestSolutionFixture.SampleSolutionPath);

        await manager.EnsureReadyOrStatusAsync(ct);

        Assert.Equal(WorkspaceState.Ready, manager.State);
        Assert.Equal([TestSolutionFixture.SampleSolutionPath], manager.LoadedSolutionPaths);
    }

    [Fact]
    public void HashText_IgnoresLineEndingsAndByteOrderMark()
    {
        Assert.Equal(ReviewAudit.HashText("a\nb\n"), ReviewAudit.HashText("﻿a\r\nb\r\n"));
        Assert.NotEqual(ReviewAudit.HashText("a\nb\n"), ReviewAudit.HashText("a\nc\n"));
    }

    [Fact]
    public void ReferencedFiles_FindsCSharpPathsAtAnyDepth()
    {
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = """{"References":[{"File":"A/One.cs","Line":3}],"Files":[{"File":"B/Two.cs","Entries":[]}],"Path":"x.csproj"}""",
                },
            ],
        };

        Assert.Equal(["A/One.cs", "B/Two.cs"], ReviewAudit.ReferencedFiles(result));
    }

    [Fact]
    public async Task RecordAsync_WritesTheSessionSolutionAndFileHashes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var manager = CreateManager(SampleRoot);
        await manager.EnsureReadyOrStatusAsync(ct);
        var audit = new ReviewAudit(manager, TimeProvider.System) { Directory = _auditDir, SessionId = "session-1" };
        var request = new CallToolRequestParams { Name = "find_symbol" };
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = """{"Symbols":[{"File":"SampleDomain/Order.cs","Line":1}]}""" }],
        };

        await audit.RecordAsync(request, result, ct);

        var line = JsonNode.Parse(File.ReadAllLines(audit.FilePath!).Single())!;
        Assert.Equal("session-1", (string?)line["session"]);
        Assert.Equal("find_symbol", (string?)line["tool"]);
        var file = line["files"]!.AsArray().Single()!;
        var onDisk = Path.Combine(SampleRoot, "SampleDomain", "Order.cs");
        Assert.Equal(Path.GetFullPath(onDisk), (string?)file["fullPath"], ignoreCase: true);
        Assert.Equal(ReviewAudit.HashText(await File.ReadAllTextAsync(onDisk, ct)), (string?)file["sha256"]);
    }
}
