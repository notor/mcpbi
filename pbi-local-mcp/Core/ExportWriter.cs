using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace pbi_local_mcp.Core;

/// <summary>
/// Writes a materialized result set to a file in a specific format.
/// </summary>
public interface IExportWriter
{
    Task WriteAsync(string fullPath, List<Dictionary<string, object?>> rows, List<string> columns);
}

/// <summary>
/// Factory: select the writer strategy by format name.
/// </summary>
public static class ExportWriter
{
    public static IExportWriter For(string format) => format.ToLowerInvariant() switch
    {
        "jsonl" => new JsonlExportWriter(),
        "csv"   => new CsvExportWriter(),
        _       => throw new ArgumentException($"Unknown export format: '{format}'. Supported: jsonl, csv.")
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// JSONL writer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Writes one JSON object per line (JSONL / newline-delimited JSON).
/// Type fidelity: strings stay strings, numbers stay numbers, DBNull → null,
/// DateTime/DateTimeOffset → ISO 8601 "o" format, booleans → true/false.
/// </summary>
public sealed class JsonlExportWriter : IExportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new DBNullJsonConverter(),
            new DateTimeJsonConverter(),
            new DateTimeOffsetJsonConverter()
        }
    };

    public async Task WriteAsync(string fullPath, List<Dictionary<string, object?>> rows, List<string> columns)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var writer = new StreamWriter(fullPath, append: false, encoding);
        foreach (var row in rows)
        {
            string line = JsonSerializer.Serialize(row, JsonOptions);
            await writer.WriteLineAsync(line);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CSV writer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Writes RFC 4180 CSV. UTF-8 without BOM. InvariantCulture everywhere.
/// Nulls → empty string, booleans → true/false, dates → ISO 8601 "o".
/// </summary>
public sealed class CsvExportWriter : IExportWriter
{
    public async Task WriteAsync(string fullPath, List<Dictionary<string, object?>> rows, List<string> columns)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await using var writer = new StreamWriter(fullPath, append: false, encoding);

        await writer.WriteLineAsync(string.Join(",", columns.Select(CsvEscape)));

        foreach (var row in rows)
        {
            var values = columns.Select(col =>
            {
                row.TryGetValue(col, out var raw);
                return CsvEscape(FormatValue(raw));
            });
            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    public static string FormatValue(object? value)
    {
        if (value is null || value is DBNull)
            return "";
        if (value is bool b)
            return b ? "true" : "false";
        if (value is DateTime dt)
            return dt.ToString("o", CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dto)
            return dto.ToString("o", CultureInfo.InvariantCulture);
        if (value is IFormattable f)
            return f.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString() ?? "";
    }

    public static string CsvEscape(string s)
    {
        bool needsQuoting = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!needsQuoting)
            return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Custom JSON converters
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Converts DBNull.Value to JSON null. Without this, System.Text.Json serializes DBNull as {}.
/// </summary>
public sealed class DBNullJsonConverter : JsonConverter<DBNull>
{
    public override DBNull Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("DBNullJsonConverter is write-only.");

    public override void Write(Utf8JsonWriter writer, DBNull value, JsonSerializerOptions options)
        => writer.WriteNullValue();
}

/// <summary>
/// Serializes DateTime as ISO 8601 round-trip format ("o") for unambiguous timestamps.
/// </summary>
public sealed class DateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("DateTimeJsonConverter is write-only.");

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("o", CultureInfo.InvariantCulture));
}

/// <summary>
/// Serializes DateTimeOffset as ISO 8601 round-trip format ("o").
/// </summary>
public sealed class DateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("DateTimeOffsetJsonConverter is write-only.");

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("o", CultureInfo.InvariantCulture));
}
