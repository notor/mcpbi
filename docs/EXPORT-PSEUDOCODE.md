# MCPBI Query Export — Pseudocode

This document describes the shape of each new function in the export feature. The actual implementation will be C#; the pseudocode below is plain English with code-shaped structure. Every function described here exists to satisfy a specific decision in `EXPORT-DESIGN.md` — read that first if any choice here looks arbitrary.

## High-level flow

When the LLM calls `ExportQueryResults`:

1. Check if exporting is enabled (`--export-dir` set)
2. Validate the LLM-supplied filename
3. Append a timestamp to the filename to prevent collisions
4. Build the full path and verify it stays inside the export directory
5. Run the DAX query against Power BI
6. Optionally apply the existing obfuscation pipeline
7. Check the row count against `--max-export-rows`
8. Write rows to disk in the requested format (JSONL or CSV)
9. Return a small receipt to the LLM

Each step is a function. The orchestration lives at the top; helpers live below.

---

## Step 1: The orchestrator (`ExportQueryResults`)

This is the new MCP tool method on `QueryExecutionTools`. It coordinates the helpers and decides what to return on each failure path.

```
function ExportQueryResults(dax, filename, format = "jsonl", applyObfuscation = true):

    // Step 1: Is exporting turned on?
    if exportConfig.ExportDir is not set:
        return error("Export is not enabled. Start the server with --export-dir.")

    // Steps 2, 3, 4: Turn the user-supplied filename into a safe full path
    try:
        fullPath = ExportPathValidator.BuildSafePath(
            filename, format, exportConfig.ExportDir)
    catch InvalidFilenameException as e:
        return error("Invalid filename: " + e.message)

    // Step 5: Run the query (uses shared helper extracted from RunQuery)
    startTime = now()
    try:
        rows, columns = ExecuteDaxAndCollect(dax)
    catch DaxValidationException as e:
        return error("DAX validation failed: " + e.message)
    catch DaxExecutionException as e:
        return error("DAX execution failed: " + e.message)

    // Step 6: Maybe scramble sensitive data (reuses existing service)
    if applyObfuscation and obfuscationService.Strategy != ObfuscationStrategy.None
       and rows.count > 0:
        obfResult = obfuscationService.ObfuscateData(rows, columns)
        rows = obfResult.Rows

    // Step 7: Did we get too many rows?
    if rows.count > exportConfig.MaxExportRows:
        return error(
            "Result has " + rows.count + " rows, " +
            "max-export-rows is " + exportConfig.MaxExportRows + ". " +
            "Refine the query or raise the limit."
        )

    // Step 8: Write the file
    try:
        writer = ExportWriter.For(format)        // selects JSONL or CSV strategy
        writer.WriteAsync(fullPath, rows, columns)
    catch IOException as e:
        return error("Failed to write file: " + e.message)

    // Step 9: Return a receipt
    return success({
        filePath: fullPath,
        rowCount: rows.count,
        columnCount: columns.count,
        format: format,
        durationMs: (now() - startTime).totalMilliseconds,
        obfuscated: applyObfuscation and obfuscationService.Strategy != None,
        obfuscationStrategy: obfuscationService.Strategy.ToString()
    })
```

Order matters: cheap checks first, so we never run a slow DAX query just to discover the filename was bad.

---

## Step 2: `ExportPathValidator.BuildSafePath`

Lives in `Core/ExportPathValidator.cs` as a static method. Takes an LLM-supplied filename and either returns a safe canonical full path or throws `InvalidFilenameException`.

```
WINDOWS_RESERVED_NAMES = [
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
]

function BuildSafePath(filename, format, exportDir):

    if filename is null, empty, or whitespace-only:
        throw InvalidFilenameException("Filename cannot be empty")

    if filename contains "/" or "\":
        throw InvalidFilenameException("Filename cannot contain path separators")

    if filename contains "..":
        throw InvalidFilenameException("Filename cannot contain '..'")

    if filename starts with drive letter (e.g. "C:") or "/" or "\":
        throw InvalidFilenameException("Filename cannot be an absolute path")

    if filename contains ":":      // alternate-data-stream defense
        throw InvalidFilenameException("Filename cannot contain ':'")

    // Strip any extension the LLM provided; we add the right one based on format
    baseName = Path.GetFileNameWithoutExtension(filename)

    if baseName is empty:
        throw InvalidFilenameException("Filename has no base name")

    // Reject Windows reserved device names (case-insensitive)
    if baseName.ToUpperInvariant() in WINDOWS_RESERVED_NAMES:
        throw InvalidFilenameException("Filename uses a reserved Windows name")

    // Append timestamp so collisions cannot occur
    timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss")
    extension = (format == "jsonl") ? ".jsonl" : ".csv"
    finalFilename = baseName + "_" + timestamp + extension

    // Combine with export directory and canonicalize both sides
    candidate = Path.Combine(exportDir, finalFilename)
    canonicalCandidate = Path.GetFullPath(candidate)
    canonicalExportDir = Path.GetFullPath(exportDir)

    // Belt-and-suspenders containment check
    expectedPrefix = canonicalExportDir + Path.DirectorySeparatorChar
    if not canonicalCandidate.StartsWith(expectedPrefix, OrdinalIgnoreCase):
        throw InvalidFilenameException("Resolved path escapes export directory")

    return canonicalCandidate
```

The final canonical-path check is the load-bearing one. Even if all earlier checks pass, this verifies the resolved path actually lives inside `exportDir`. It's there in case some clever input slips through.

---

## Step 3: `ExecuteDaxAndCollect` (shared helper extracted from `RunQuery`)

This is a refactor, not new logic. The existing `RunQuery` method in `QueryExecutionTools.cs` has ~60 lines that validate DAX, build the executable query, and call `ITabularConnection.ExecAsync`. Extract that into a private helper so both `RunQuery` and `ExportQueryResults` use it.

```
private async function ExecuteDaxAndCollect(dax):

    if dax is null, empty, or whitespace:
        throw ArgumentException("DAX query cannot be null or empty")

    query = dax.Trim()

    // Reuse existing validation
    validationErrors = ValidateQuerySyntax(query)
    if validationErrors is non-empty:
        throw DaxValidationException(join(validationErrors, "; "))

    if query contains "DEFINE" (case-insensitive):
        ValidateCompleteDAXQuery(query)

    // Reuse existing query-construction logic
    if query starts with "DEFINE" or "EVALUATE":
        finalQuery = query
    else:
        finalQuery = ConstructEvaluateStatement(query, topN: int.MaxValue)
        // Note: pass int.MaxValue here because the export tool has its own
        // row-limit enforcement; we don't want RunQuery's TOPN to clip rows.

    // Execute via existing connection
    rawResult = await tabularConnection.ExecAsync(finalQuery, QueryType.DAX)

    rows = rawResult as List<Dictionary<string, object?>>
        ?? new List<Dictionary<string, object?>>()
    columns = (rows.count == 0) ? [] : rows[0].Keys.ToList()

    return (rows, columns)
```

After extracting, `RunQuery` calls this helper and then continues with its existing truncation/obfuscation/response-formatting logic. Behavior of `RunQuery` should be unchanged.

---

## Step 4: `ExportWriter` interface and strategies

Lives in `Core/ExportWriter.cs`. One small interface, two implementations.

```
interface IExportWriter:
    async function WriteAsync(fullPath, rows, columns)

class ExportWriter:
    static function For(format) -> IExportWriter:
        switch format.ToLowerInvariant():
            case "jsonl": return new JsonlExportWriter()
            case "csv":   return new CsvExportWriter()
            default:      throw ArgumentException("Unknown format: " + format)
```

---

## Step 4a: JSONL writer (the default)

```
class JsonlExportWriter implements IExportWriter:

    static jsonOptions = new JsonSerializerOptions:
        WriteIndented: false
        Encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            // (so we don't escape every non-ASCII character; UTF-8 handles them)
        Converters: [
            new DBNullJsonConverter(),
            new DateTimeJsonConverter(),
            new DateTimeOffsetJsonConverter()
        ]

    async function WriteAsync(fullPath, rows, columns):
        // UTF-8 without BOM — Python and most tools prefer no BOM
        encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        using writer = new StreamWriter(fullPath, append: false, encoding):
            for each row in rows:
                jsonLine = JsonSerializer.Serialize(row, jsonOptions)
                await writer.WriteLineAsync(jsonLine)
```

The two key custom converters:

```
class DBNullJsonConverter extends JsonConverter<object>:
    function CanConvert(type) -> bool:
        return type == typeof(DBNull)

    function Write(writer, value, options):
        writer.WriteNullValue()

    function Read(...):
        throw NotSupportedException()    // we only write
```

```
class DateTimeJsonConverter extends JsonConverter<DateTime>:
    function Write(writer, value, options):
        // ISO 8601 round-trip format ("o") gives unambiguous timestamps
        writer.WriteStringValue(value.ToString("o", InvariantCulture))

    function Read(...):
        throw NotSupportedException()
```

`DBNull.Value` is the value ADOMD returns for missing data. Without the converter, `System.Text.Json` serializes it as `{}`, which would be a quiet disaster — every null in your data would become an empty object on disk. The converter forces it to JSON `null`, which `pd.read_json` correctly reads as `NaN`.

---

## Step 4b: CSV writer (fallback)

```
class CsvExportWriter implements IExportWriter:

    async function WriteAsync(fullPath, rows, columns):
        encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        using writer = new StreamWriter(fullPath, append: false, encoding):

            // Header row
            headerLine = string.Join(",", columns.Select(CsvEscape))
            await writer.WriteLineAsync(headerLine)

            // Data rows
            for each row in rows:
                values = []
                for each column in columns:
                    raw = row.TryGetValue(column) ? value : null
                    formatted = FormatValueAsString(raw)
                    escaped = CsvEscape(formatted)
                    values.Add(escaped)
                await writer.WriteLineAsync(string.Join(",", values))
```

```
static function FormatValueAsString(value):
    if value is null or value is DBNull:
        return ""
    if value is DateTime dt:
        return dt.ToString("o", InvariantCulture)
    if value is DateTimeOffset dto:
        return dto.ToString("o", InvariantCulture)
    if value is IFormattable f:                  // catches decimal, double, int, etc.
        return f.ToString(null, InvariantCulture)
    if value is bool b:
        return b ? "true" : "false"
    return value.ToString() ?? ""
```

```
static function CsvEscape(s):
    needsQuoting = s.contains(",") or s.contains("\"")
                or s.contains("\n") or s.contains("\r")
    if needsQuoting:
        doubled = s.Replace("\"", "\"\"")
        return "\"" + doubled + "\""
    return s
```

`InvariantCulture` everywhere is critical: it forces "." as the decimal separator regardless of the host machine's locale, so CSVs are portable.

---

## Step 5: New configuration

Add a new `ExportConfig` class in `Configuration/`:

```
class ExportConfig:
    string? ExportDir            // null = export disabled
    int MaxExportRows = 100000
```

Wire two new CLI flags in `Resources/Server.cs`'s `ProcessCommandLineArgumentsAsync`:

```
exportDirOption = new Option<string?>(
    "--export-dir",
    "Directory where ExportQueryResults writes files. Required to enable export.")

maxExportRowsOption = new Option<int>(
    "--max-export-rows",
    "Maximum rows per export. Exceeding this fails the export.",
    getDefaultValue: () => 100000)
```

Set environment variables (consistent with existing pattern):

```
PBI_EXPORT_DIR        // unset = disabled
PBI_MAX_EXPORT_ROWS   // default "100000"
```

When `--export-dir` is provided:
- Resolve it via `Path.GetFullPath` (handles relative paths)
- Verify the directory exists; if not, create it
- Verify it's writable (try opening a temp file)
- Set `PBI_EXPORT_DIR` to the canonical path

Wire the DI registration alongside the existing services:

```
.AddSingleton<ExportConfig>(_ =>
{
    return new ExportConfig
    {
        ExportDir = Environment.GetEnvironmentVariable("PBI_EXPORT_DIR"),
        MaxExportRows = int.TryParse(
            Environment.GetEnvironmentVariable("PBI_MAX_EXPORT_ROWS"),
            out var n) && n > 0 ? n : 100000
    };
})
```

Inject `ExportConfig` into `QueryExecutionTools` via constructor.

---

## Step 6: Tests

Add `pbi-local-mcp.Tests/ExportTests.cs` covering:

**Path validation (`ExportPathValidator`)**
- Rejects empty/whitespace filenames
- Rejects forward slashes, backslashes, `..`
- Rejects absolute paths (`C:\foo`, `/foo`, `\foo`)
- Rejects colons (ADS syntax)
- Rejects each Windows reserved name (CON, PRN, AUX, NUL, COM1-9, LPT1-9), case-insensitive
- Strips LLM-provided extensions and applies format-correct one
- Appends timestamp suffix
- Resolved path stays inside export-dir

**JSONL writer**
- Round-trips strings, integers, decimals, doubles, booleans, dates
- `DBNull.Value` becomes JSON `null`, not `{}`
- Leading-zero string IDs (`"00742"`) preserve their leading zeros
- Empty result set produces empty file (no header, no rows)
- File is UTF-8 without BOM

**CSV writer**
- RFC 4180 escaping for commas, quotes, newlines
- Numeric values use `.` as decimal separator regardless of host locale
- Booleans render as `true`/`false`
- Nulls become empty strings
- ISO 8601 dates

**Tool integration**
- Returns "not enabled" error when `ExportDir` is unset
- Returns "row limit exceeded" error when count > max
- Receipt includes correct filePath, rowCount, columnCount
- Obfuscation default is on; obfuscated values appear in output file
- `applyObfuscation: false` produces unscrambled output
- DAX validation errors surface as tool errors, not exceptions

---

## What stays the same

- `ITabularConnection` interface unchanged
- `DataObfuscationService` unchanged
- `DaxSecurityUtils` unchanged
- `RunQuery` behavior unchanged (only refactored to call shared helper)
- All other existing tools unchanged

The new code adds two files (`Core/ExportPathValidator.cs`, `Core/ExportWriter.cs`), one config class (`Configuration/ExportConfig.cs`), one tool method on the existing `QueryExecutionTools.cs`, two CLI flags, and one test file.
