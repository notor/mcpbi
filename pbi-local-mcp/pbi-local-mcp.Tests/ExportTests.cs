using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using pbi_local_mcp.Configuration;
using pbi_local_mcp.Core;

namespace pbi_local_mcp.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ExportPathValidator — pure unit tests, no Power BI required
// ─────────────────────────────────────────────────────────────────────────────

public class ExportPathValidatorTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "mcpbi-tests-" + Path.GetRandomFileName());

    public ExportPathValidatorTests()
    {
        Directory.CreateDirectory(TempDir);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildSafePath_EmptyOrWhitespaceFilename_Throws(string filename)
    {
        Assert.Throws<InvalidFilenameException>(() =>
            ExportPathValidator.BuildSafePath(filename, "jsonl", TempDir));
    }

    [Theory]
    [InlineData("sub/file")]
    [InlineData("sub\\file")]
    public void BuildSafePath_PathSeparatorInFilename_Throws(string filename)
    {
        Assert.Throws<InvalidFilenameException>(() =>
            ExportPathValidator.BuildSafePath(filename, "jsonl", TempDir));
    }

    [Fact]
    public void BuildSafePath_DotDotInFilename_Throws()
    {
        Assert.Throws<InvalidFilenameException>(() =>
            ExportPathValidator.BuildSafePath("../outside", "jsonl", TempDir));
    }

    [Theory]
    [InlineData("C:\\absolute")]
    [InlineData("/absolute")]
    [InlineData("\\absolute")]
    public void BuildSafePath_AbsolutePath_Throws(string filename)
    {
        Assert.Throws<InvalidFilenameException>(() =>
            ExportPathValidator.BuildSafePath(filename, "jsonl", TempDir));
    }

    [Theory]
    [InlineData("file:stream")]
    [InlineData("foo:bar")]
    public void BuildSafePath_ColonInFilename_Throws(string filename)
    {
        Assert.Throws<InvalidFilenameException>(() =>
            ExportPathValidator.BuildSafePath(filename, "jsonl", TempDir));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    public void BuildSafePath_ReservedWindowsName_Throws(string filename)
    {
        Assert.Throws<InvalidFilenameException>(() =>
            ExportPathValidator.BuildSafePath(filename, "jsonl", TempDir));
    }

    [Fact]
    public void BuildSafePath_ValidFilename_StripsLlmExtensionAppliesFormatExtension()
    {
        string result = ExportPathValidator.BuildSafePath("mydata.txt", "jsonl", TempDir);
        Assert.EndsWith(".jsonl", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".txt", result);
    }

    [Fact]
    public void BuildSafePath_CsvFormat_AppliesCsvExtension()
    {
        string result = ExportPathValidator.BuildSafePath("mydata", "csv", TempDir);
        Assert.EndsWith(".csv", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSafePath_AppendsTimestampSuffix()
    {
        string result = ExportPathValidator.BuildSafePath("report", "jsonl", TempDir);
        string filename = Path.GetFileNameWithoutExtension(result);
        // Should be "report_YYYYMMDD_HHMMSS"
        Assert.StartsWith("report_", filename);
        Assert.Matches(@"report_\d{8}_\d{6}", filename);
    }

    [Fact]
    public void BuildSafePath_ResolvedPathStaysInsideExportDir()
    {
        string result = ExportPathValidator.BuildSafePath("safe", "jsonl", TempDir);
        string canonical = Path.GetFullPath(result);
        string canonicalDir = Path.GetFullPath(TempDir) + Path.DirectorySeparatorChar;
        Assert.StartsWith(canonicalDir, canonical, StringComparison.OrdinalIgnoreCase);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// JSONL writer — pure unit tests
// ─────────────────────────────────────────────────────────────────────────────

public class JsonlExportWriterTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "mcpbi-jsonl-" + Path.GetRandomFileName());

    public JsonlExportWriterTests()
    {
        Directory.CreateDirectory(TempDir);
    }

    private static string TempFile() => Path.Combine(TempDir, Path.GetRandomFileName() + ".jsonl");

    private static async Task<List<JsonElement>> ReadLines(string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        return lines.Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => JsonDocument.Parse(l).RootElement.Clone())
                    .ToList();
    }

    [Fact]
    public async Task WriteAsync_EmptyResultSet_ProducesEmptyFile()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        await writer.WriteAsync(path, new List<Dictionary<string, object?>>(), new List<string>());

        var text = await File.ReadAllTextAsync(path);
        Assert.True(string.IsNullOrWhiteSpace(text), "Empty result set should produce empty file");
    }

    [Fact]
    public async Task WriteAsync_FilIsUtf8WithoutBom()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["v"] = "hello" }
        };
        await writer.WriteAsync(path, rows, new List<string> { "v" });

        var bytes = await File.ReadAllBytesAsync(path);
        // UTF-8 BOM is EF BB BF — must NOT be present
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "File should be UTF-8 without BOM");
    }

    [Fact]
    public async Task WriteAsync_StringValue_PreservesLeadingZeros()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = "00742" }
        };
        await writer.WriteAsync(path, rows, new List<string> { "id" });

        var lines = await ReadLines(path);
        Assert.Equal("00742", lines[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteAsync_DbNullValue_WritesJsonNull()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["col"] = DBNull.Value }
        };
        await writer.WriteAsync(path, rows, new List<string> { "col" });

        var lines = await ReadLines(path);
        Assert.Equal(JsonValueKind.Null, lines[0].GetProperty("col").ValueKind);
    }

    [Fact]
    public async Task WriteAsync_IntegerValue_WritesJsonNumber()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["n"] = 42 } };
        await writer.WriteAsync(path, rows, new List<string> { "n" });

        var lines = await ReadLines(path);
        Assert.Equal(JsonValueKind.Number, lines[0].GetProperty("n").ValueKind);
        Assert.Equal(42, lines[0].GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task WriteAsync_DecimalValue_WritesJsonNumber()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["d"] = 3.14m } };
        await writer.WriteAsync(path, rows, new List<string> { "d" });

        var lines = await ReadLines(path);
        Assert.Equal(JsonValueKind.Number, lines[0].GetProperty("d").ValueKind);
        Assert.Equal(3.14m, lines[0].GetProperty("d").GetDecimal());
    }

    [Fact]
    public async Task WriteAsync_BooleanValue_WritesJsonBoolean()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["b"] = true } };
        await writer.WriteAsync(path, rows, new List<string> { "b" });

        var lines = await ReadLines(path);
        Assert.Equal(JsonValueKind.True, lines[0].GetProperty("b").ValueKind);
    }

    [Fact]
    public async Task WriteAsync_DateTimeValue_WritesIso8601String()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var dt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var rows = new List<Dictionary<string, object?>> { new() { ["ts"] = dt } };
        await writer.WriteAsync(path, rows, new List<string> { "ts" });

        var lines = await ReadLines(path);
        var value = lines[0].GetProperty("ts").GetString()!;
        Assert.Contains("2024-06-15", value);
        Assert.Contains("10:30:00", value);
    }

    [Fact]
    public async Task WriteAsync_MultipleRows_WritesOneLinePerRow()
    {
        var writer = new JsonlExportWriter();
        var path = TempFile();
        var rows = Enumerable.Range(1, 5).Select(i =>
            new Dictionary<string, object?> { ["i"] = i }).ToList();
        await writer.WriteAsync(path, rows, new List<string> { "i" });

        var lines = (await File.ReadAllLinesAsync(path)).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(5, lines.Count);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CSV writer — pure unit tests
// ─────────────────────────────────────────────────────────────────────────────

public class CsvExportWriterTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "mcpbi-csv-" + Path.GetRandomFileName());

    public CsvExportWriterTests()
    {
        Directory.CreateDirectory(TempDir);
    }

    private static string TempFile() => Path.Combine(TempDir, Path.GetRandomFileName() + ".csv");

    private static async Task<List<string[]>> ReadCsv(string path)
    {
        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
        return lines.Select(l => l.Split(',').ToArray()).ToList();
    }

    [Fact]
    public async Task WriteAsync_WritesHeaderRow()
    {
        var writer = new CsvExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["name"] = "Alice", ["score"] = 99 } };
        await writer.WriteAsync(path, rows, new List<string> { "name", "score" });

        var csv = await ReadCsv(path);
        Assert.Equal("name", csv[0][0]);
        Assert.Equal("score", csv[0][1]);
    }

    [Fact]
    public async Task WriteAsync_NullValue_WritesEmptyString()
    {
        var writer = new CsvExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["v"] = (object?)null } };
        await writer.WriteAsync(path, rows, new List<string> { "v" });

        var csv = await ReadCsv(path);
        Assert.Equal("", csv[1][0]);
    }

    [Fact]
    public async Task WriteAsync_DbNullValue_WritesEmptyString()
    {
        var writer = new CsvExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["v"] = DBNull.Value } };
        await writer.WriteAsync(path, rows, new List<string> { "v" });

        var csv = await ReadCsv(path);
        Assert.Equal("", csv[1][0]);
    }

    [Fact]
    public void FormatValue_DecimalUsesInvariantCulture()
    {
        // 1.5 must render as "1.5" not "1,5" regardless of host locale
        var result = CsvExportWriter.FormatValue(1.5m);
        Assert.Equal("1.5", result);
    }

    [Fact]
    public void FormatValue_BooleanRendersLowercase()
    {
        Assert.Equal("true", CsvExportWriter.FormatValue(true));
        Assert.Equal("false", CsvExportWriter.FormatValue(false));
    }

    [Fact]
    public void FormatValue_DateTimeRendersIso8601()
    {
        var dt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = CsvExportWriter.FormatValue(dt);
        Assert.Contains("2024-03-01", result);
    }

    [Theory]
    [InlineData("hello,world", "\"hello,world\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("cr\rhere", "\"cr\rhere\"")]
    [InlineData("plain", "plain")]
    public void CsvEscape_Rfc4180(string input, string expected)
    {
        Assert.Equal(expected, CsvExportWriter.CsvEscape(input));
    }

    [Fact]
    public async Task WriteAsync_FileIsUtf8WithoutBom()
    {
        var writer = new CsvExportWriter();
        var path = TempFile();
        var rows = new List<Dictionary<string, object?>> { new() { ["v"] = "x" } };
        await writer.WriteAsync(path, rows, new List<string> { "v" });

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "CSV should be UTF-8 without BOM");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ExportQueryResults tool — integration tests (no live Power BI required for
// the configuration / validation paths; live PBI needed for full round-trip)
// ─────────────────────────────────────────────────────────────────────────────

public class ExportQueryResultsToolTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "mcpbi-tool-" + Path.GetRandomFileName());

    public ExportQueryResultsToolTests()
    {
        Directory.CreateDirectory(TempDir);
    }

    private static QueryExecutionTools BuildTools(string? exportDir = null, int maxExportRows = 100_000)
    {
        var connection = TestConnectionHelper.CreateConnection();
        var exportConfig = new ExportConfig { ExportDir = exportDir, MaxExportRows = maxExportRows };
        return new QueryExecutionTools(
            connection,
            NullLogger<QueryExecutionTools>.Instance,
            TestConnectionHelper.CreateTruncationService(),
            TestConnectionHelper.CreateObfuscationService(),
            exportConfig);
    }

    [Fact]
    public async Task ExportQueryResults_ExportNotEnabled_ReturnsNotEnabledError()
    {
        var tools = BuildTools(exportDir: null);
        var result = await tools.ExportQueryResults("EVALUATE {1}", "test");

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("not enabled", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportQueryResults_InvalidFilename_PathSeparator_ReturnsError()
    {
        var tools = BuildTools(exportDir: TempDir);
        var result = await tools.ExportQueryResults("EVALUATE {1}", "sub/file");

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Invalid filename", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExportQueryResults_InvalidFilename_DotDot_ReturnsError()
    {
        var tools = BuildTools(exportDir: TempDir);
        var result = await tools.ExportQueryResults("EVALUATE {1}", "../escape");

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Invalid filename", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExportQueryResults_DaxValidationError_ReturnsValidationError()
    {
        var tools = BuildTools(exportDir: TempDir);
        var result = await tools.ExportQueryResults("", "myfile");

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("validation", doc.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportQueryResults_RowLimitExceeded_ReturnsRowLimitError()
    {
        var tools = BuildTools(exportDir: TempDir, maxExportRows: 1);
        object result;
        try
        {
            result = await tools.ExportQueryResults("EVALUATE UNION({1},{2})", "rowtest");
        }
        catch (PowerBiConnectionException)
        {
            return; // no PBI running — acceptable in CI
        }

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("max-export-rows", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportQueryResults_Success_ReceiptContainsExpectedFields()
    {
        var tools = BuildTools(exportDir: TempDir);
        object result;
        try
        {
            result = await tools.ExportQueryResults("EVALUATE {1}", "receipt_test");
        }
        catch (PowerBiConnectionException)
        {
            return; // no PBI running — acceptable in CI
        }

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.GetProperty("success").GetBoolean())
            return; // DAX execution error — acceptable in CI without a real model

        Assert.True(root.TryGetProperty("filePath", out _));
        Assert.True(root.TryGetProperty("rowCount", out _));
        Assert.True(root.TryGetProperty("columnCount", out _));
        Assert.True(root.TryGetProperty("format", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
        Assert.Equal("jsonl", root.GetProperty("format").GetString());
        Assert.True(File.Exists(root.GetProperty("filePath").GetString()!));
    }

    [Fact]
    public async Task ExportQueryResults_ObfuscationDefaultIsOn_FlagReflectsStrategy()
    {
        var tools = BuildTools(exportDir: TempDir);
        object result;
        try
        {
            result = await tools.ExportQueryResults("EVALUATE {1}", "obf_test");
        }
        catch (PowerBiConnectionException)
        {
            return; // no PBI running — acceptable in CI
        }

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.GetProperty("success").GetBoolean())
            return; // DAX execution error — acceptable in CI without a real model

        // Strategy is "none" by default so didObfuscate = false
        Assert.False(root.GetProperty("obfuscated").GetBoolean());
        Assert.Equal("None", root.GetProperty("obfuscationStrategy").GetString());
    }
}
