namespace CWM.RoslynNavigator.Tests;

public class SourcePathsTests
{
    [Theory]
    [InlineData(@"C:\repo\src\Sdk\Codes.cs", "src/Sdk/Codes.cs")]
    [InlineData(@"C:\repo\src\Sdk\Codes.cs", @"src\Sdk\Codes.cs")]
    [InlineData(@"C:\repo\src\Sdk\Codes.cs", "./src/Sdk/Codes.cs")]
    [InlineData(@"C:\repo\src\Sdk\Codes.cs", "codes.CS")]
    [InlineData("/repo/src/Sdk/Codes.cs", @"Sdk\Codes.cs")]
    [InlineData("/repo/src/Sdk/Codes.cs", "/repo/src/Sdk/Codes.cs")]
    public void EndsWithPath_MatchesTrailingSegments_WithEitherSeparator(string filePath, string hint)
    {
        Assert.True(SourcePaths.EndsWithPath(filePath, hint));
    }

    [Theory]
    [InlineData(@"C:\repo\src\Sdk\GameCodes.cs", "Codes.cs")]
    [InlineData(@"C:\repo\src\Sdk\Codes.cs", "other/Sdk/Codes.cs")]
    [InlineData(@"C:\repo\src\Sdk\Codes.cs", "")]
    [InlineData(null, "Codes.cs")]
    public void EndsWithPath_RejectsPartialSegmentsAndOtherFolders(string? filePath, string hint)
    {
        Assert.False(SourcePaths.EndsWithPath(filePath, hint));
    }

    [Fact]
    public void ContainsPath_MatchesAFolderWithEitherSeparator()
    {
        Assert.True(SourcePaths.ContainsPath(@"C:\repo\src\Sdk\Codes.cs", "src/Sdk"));
        Assert.False(SourcePaths.ContainsPath(@"C:\repo\src\Sdk\Codes.cs", "tests/"));
    }
}
