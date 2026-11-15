using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace CWM.RoslynNavigator;

/// <summary>
/// With <c>--audit-dir</c>: appends one JSON line per tool call to a file of this process's own in that
/// folder, so a commit-review check can tell that a reviewer's claims came from this server, run on the code
/// under review. Each line names the client session (<c>CLAUDE_CODE_SESSION_ID</c>, which Claude Code sets
/// for the stdio servers it starts), the loaded solutions, the tool and its arguments, and every source
/// file the result points at, with the SHA-256 of that file's text as the workspace held it.
/// </summary>
/// <remarks>
/// The hash covers the document text with a leading byte-order mark removed and CRLF turned into LF,
/// encoded as UTF-8, so a checker can compute the same value from a git blob.
/// </remarks>
public sealed class ReviewAudit(WorkspaceManager workspace, TimeProvider clock)
{
    /// <summary>The command-line option that turns the record on and names its folder.</summary>
    public const string Flag = "--audit-dir";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _filePath;

    /// <summary>The folder the record is written to; null turns the record off.</summary>
    public string? Directory { get; set; }

    /// <summary>The client session that started this server, or null when the client doesn't say.</summary>
    public string? SessionId { get; set; } = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID");

    /// <summary>The file this process writes, chosen on first use.</summary>
    public string? FilePath => _filePath;

    public async Task RecordAsync(CallToolRequestParams? request, CallToolResult result, CancellationToken ct)
    {
        if (Directory is null || request is null) return;

        var files = new JsonArray();
        foreach (var relative in ReferencedFiles(result).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (fullPath, hash) = await HashDocumentAsync(relative, ct);
            files.Add(new JsonObject { ["path"] = relative, ["fullPath"] = fullPath, ["sha256"] = hash });
        }

        var line = new JsonObject
        {
            ["utc"] = clock.GetUtcNow().ToString("O"),
            ["session"] = SessionId,
            ["pid"] = Environment.ProcessId,
            ["solutions"] = new JsonArray([.. workspace.LoadedSolutionPaths.Select(p => (JsonNode?)p)]),
            ["state"] = workspace.State.ToString(),
            ["tool"] = request.Name,
            ["arguments"] = request.Arguments is null ? null : JsonSerializer.SerializeToNode(request.Arguments),
            ["isError"] = result.IsError ?? false,
            ["files"] = files,
        };

        await _gate.WaitAsync(ct);
        try
        {
            if (_filePath is null)
            {
                System.IO.Directory.CreateDirectory(Directory);
                _filePath = Path.Combine(Directory, $"{clock.GetUtcNow():yyyyMMddTHHmmssZ}-{Environment.ProcessId}.jsonl");
            }
            await File.AppendAllTextAsync(_filePath, line.ToJsonString() + "\n", ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The full path and SHA-256 of the solution document a tool printed as <paramref name="relativePath"/>,
    /// as the workspace holds it; the hash is null when the path is no document of the loaded solutions.
    /// </summary>
    internal async Task<(string? FullPath, string? Sha256)> HashDocumentAsync(string relativePath, CancellationToken ct)
    {
        var solution = workspace.GetSolution();
        var root = workspace.SolutionDirectory;
        if (solution is null || root is null) return (null, null);

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var documentId = solution.GetDocumentIdsWithFilePath(fullPath).FirstOrDefault();
        if (documentId is null || solution.GetDocument(documentId) is not { } document) return (fullPath, null);

        var text = (await document.GetTextAsync(ct)).ToString();
        return (fullPath, HashText(text));
    }

    /// <summary>The hash this record uses for a file's text (see the class remarks).</summary>
    public static string HashText(string text)
    {
        if (text.StartsWith('﻿')) text = text[1..];
        text = text.Replace("\r\n", "\n");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Every C# file a <c>File</c>, <c>Path</c> or <c>TestFile</c> value in the result's JSON names.</summary>
    internal static IEnumerable<string> ReferencedFiles(CallToolResult result)
    {
        foreach (var block in result.Content)
        {
            if (block is not TextContentBlock { Text: { } text }) continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (var path in Walk(node))
                yield return path;
        }
    }

    private static IEnumerable<string> Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key is "File" or "Path" or "TestFile"
                        && value is JsonValue v && v.TryGetValue<string>(out var s)
                        && s.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return s;
                        continue;
                    }
                    foreach (var inner in Walk(value))
                        yield return inner;
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    foreach (var inner in Walk(item))
                        yield return inner;
                break;
        }
    }
}
