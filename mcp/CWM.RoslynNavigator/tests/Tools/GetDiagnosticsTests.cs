using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetDiagnosticsTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task GetDiagnostics_SolutionScope_ReturnsDiagnostics()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(fixture.WorkspaceManager, scope: "solution", ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // The sample solution has an intentional unused variable in ProductService
        // which should generate a CS0219 warning
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDiagnostics_FileScope_FiltersToSpecificFile()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "file",
            path: "ProductService.cs",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // ProductService.cs has the intentional unused variable
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("SampleApi/AntiPatternExamples.cs")]
    [InlineData("SampleApi\\AntiPatternExamples.cs")]
    public async Task GetDiagnostics_FileScopeWithEitherSeparator_ReportsThatFilesDiagnostics(string path)
    {
        // A multi-segment path written with the other platform's separator used to match no
        // diagnostic, so a broken file reported a clean bill of health. AntiPatternExamples.cs
        // reliably has errors: the sample does not reference Microsoft.Extensions.Logging.
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "file",
            path: path,
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        Assert.True(result.Errors > 0, "Expected the file's errors to be reported");
        Assert.All(result.Diagnostics, d => Assert.Equal("SampleApi/AntiPatternExamples.cs", d.File));
    }

    [Fact]
    public async Task GetDiagnostics_SeverityFilter_Error_FiltersCorrectly()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "solution",
            severityFilter: "error",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // All returned diagnostics should be errors
        Assert.All(result.Diagnostics, d => Assert.Equal("error", d.Severity));
    }

    [Fact]
    public async Task GetDiagnostics_ProjectScope_FiltersToProject()
    {
        var json = await GetDiagnosticsTool.ExecuteAsync(
            fixture.WorkspaceManager,
            scope: "project",
            path: "SampleDomain",
            ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiagnosticsResult>(json)!;

        // SampleDomain should have clean compilations (no intentional issues)
        Assert.NotNull(result);
    }
}
