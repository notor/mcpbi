# MCPBI Query Export — Design Doc

## Context

MCPBI is a local MCP server that bridges LLM clients to a Power BI Desktop tabular model. Its existing tools are read-only and return query results in MCP responses, capped at 500 rows by default to fit the LLM's context window. This works for conversational analytics but breaks down when the workflow needs the *data itself* — for example, feeding query output to a downstream Python script that generates a report. Result sets in the 1K–50K row range are too large for the LLM to relay yet too small to justify writing custom DAX export scripts.

This document describes a new MCP tool, `ExportQueryResults`, that materializes query results to a file on the local filesystem. The actual rows skip the LLM entirely; only a small receipt (`{filePath, rowCount, columnCount, format, durationMs}`) is returned to the model.

## Goals

Enable LLM-driven workflows where the LLM constructs a DAX query but a downstream automated consumer reads the data. Preserve MCPBI's read-only-by-default character: write capability is opt-in via a new startup flag, and disabled deployments behave exactly as today. Match the project's existing security posture (bounded write surface, obfuscation defaults, explicit failure modes).

## Non-goals

Streaming execution. Public-internet hardening of the path validator (deployment is single-user on a Windows VM). Multiple concurrent exports. Job/handle patterns for very long-running queries. Writing to network shares or arbitrary user-specified paths.

## Design

### Tool surface

```
ExportQueryResults(
  dax: string,
  filename: string,
  format: "jsonl" | "csv" = "jsonl",
  applyObfuscation: bool = true
) → { filePath, rowCount, columnCount, format, durationMs }
```

The LLM passes a DAX query and a filename. The server validates, executes, optionally applies the existing obfuscation pipeline, and writes the file. The tool is synchronous; queries that exceed the 5-minute MCP timeout fail.

### Format

Default is JSONL (newline-delimited JSON). Each row is a JSON object whose keys are column names and whose values preserve their CLR types from ADOMD: strings as strings, numbers as JSON numbers, nulls as JSON `null`, datetimes as ISO 8601 with offset, booleans as JSON booleans. `DBNull.Value` from ADOMD is converted to JSON `null` via a custom serializer.

JSONL is the default because the consumer is automated — a Python script using `pd.read_json(path, lines=True)`. Type fidelity matters: CSV's stringification + pandas inference path silently corrupts leading-zero string IDs, large integers, and ambiguous date-shaped strings. JSONL eliminates the entire class of inference-related bugs.

CSV is supported as an opt-in fallback for the occasional case of opening a file in Excel for spot-checking. CSV writes use UTF-8 without BOM, RFC 4180 escaping, ISO 8601 dates, lowercase `true`/`false` for booleans, empty string for nulls.

### File destination

A new server startup flag, `--export-dir <path>`, configures a single directory where exports land. If unset, `ExportQueryResults` returns an explicit error directing the operator to enable it; the server's existing read-only behavior is unchanged.

The tool accepts a *filename only*, not a path. The server validates the filename (rejects path separators, traversal sequences, absolute paths, reserved Windows device names, alternate-data-stream syntax), appends a timestamp suffix (`name_YYYYMMDD_HHMMSS.ext`) so collisions cannot occur, and joins it to `--export-dir`. The resulting path is canonicalized via `Path.GetFullPath` and verified to remain inside the export directory before any file handle is opened.

Path validation targets the realistic threat — the LLM hallucinating a weird filename — not adversarial bypass. Standard .NET path utilities plus the validation rules above are sufficient for single-user Windows VM deployment.

### Row limit

A separate startup flag, `--max-export-rows`, defaults to 100,000. Rows are materialized into memory before the file is opened (the buffered-then-check strategy). If the materialized count exceeds the limit, the tool returns an error specifying the actual count and the configured limit; no file is written. This trades worst-case memory for simplicity and avoids the partial-file-cleanup problem of streaming. At target sizes (1K–50K rows), memory pressure is negligible.

Silent truncation is explicitly rejected. Exports exist to feed automated downstream consumers; a partial file that looks complete is the worst possible failure mode because it produces wrong reports nobody can detect.

### Obfuscation

The existing `DataObfuscationService` runs on rows before they reach the writer, controlled by the standard `--obfuscation-strategy` server flag. The tool exposes an `applyObfuscation` parameter defaulting to `true`. Files on disk are precisely the artifact obfuscation was designed to protect; defaulting off would silently subvert the existing security model.

### Failure modes

Each gate produces an explicit, actionable error returned to the LLM as the tool result:

- Export not enabled (`--export-dir` unset)
- Invalid filename (separator, traversal, reserved name, ADS syntax, absolute path)
- DAX validation or execution failure (existing `RunQuery` validation pipeline)
- Row count exceeds `--max-export-rows`
- Query exceeds 5-minute timeout (surfaced as MCP-level cancellation)

No partial files are ever written. Validation order is cheap-first: configuration, filename, DAX validation, execution, row count, write.

## Implementation

The new code is small and isolated:

- New tool method `ExportQueryResults` in `Tools/QueryExecutionTools.cs`, structurally parallel to `RunQuery`. The shared validation and query-construction logic from `RunQuery` is extracted into a private helper used by both.
- New `Core/ExportWriter.cs` exposing two writer strategies (JSONL and CSV) behind a common interface. JSONL uses `System.Text.Json` with a configured `JsonSerializerOptions` that handles `DBNull`, datetimes, and decimals correctly. CSV uses RFC 4180 escaping with explicit type formatting.
- New `Core/ExportPathValidator.cs` for filename validation, timestamp suffixing, and canonical-path containment checks.
- Two new CLI flags wired through `Resources/Server.cs`'s `ProcessCommandLineArgumentsAsync`: `--export-dir` (env var `PBI_EXPORT_DIR`) and `--max-export-rows` (env var `PBI_MAX_EXPORT_ROWS`). Read by the DI registration in `ConfigureServerAsync` and surfaced via a new `ExportConfig` injectable.
- Tests for path traversal rejection, reserved-name rejection, row-limit overflow, obfuscation correctness in JSONL output (verifying scrambled values are correctly typed), JSONL type fidelity for the full set of ADOMD return types, and the not-configured error path.
- README updates noting that the server can write files when explicitly enabled and documenting both flags.

No changes to `ITabularConnection`, `DataObfuscationService`, `DaxSecurityUtils`, or any existing tool. Estimated effort: an afternoon of coding plus tests and documentation.

## Risks

**Scope creep on the write surface.** Once the server can write one file format to one directory, future requests for additional formats, additional destinations, or removal of restrictions become incrementally easier to justify. Mitigated by stating non-goals explicitly and treating future expansions as separate proposals on their own merits.

**Type-fidelity regressions in JSONL output.** The `JsonSerializerOptions` configuration is the load-bearing piece for the "JSONL eliminates silent corruption" argument. Tests must cover every ADOMD-returnable type, not just the common ones, or we recreate the CSV-style silent-corruption problem we were trying to avoid.

**Memory pressure at the row limit.** Materializing 100,000 rows of typical PBI output in a `List<Dictionary<string, object?>>` is acceptable but not free. If the limit is raised significantly later, streaming becomes a real refactor. Acceptable at current scope; documented as a future concern.

**The 5-minute timeout is a hard ceiling.** Long-running DAX queries will fail, possibly after producing useful work. Acceptable for personal-use deployment; would need revisiting if this ever grew to multi-user.

## Decisions log

| Decision | Choice | Alternative rejected |
|---|---|---|
| File destination | Configured `--export-dir`, filename-only | User-specified absolute path (LLM has to invent paths; expands trust surface) |
| Default format | JSONL | CSV (silent type-corruption risk for automated Python consumer) |
| Execution model | Buffered, materialize-then-check | Streaming with mid-flight abort (unneeded at target row counts) |
| Row-limit behavior | Refuse with explicit error | Silent truncation (produces wrong reports invisibly) |
| Obfuscation default | On | Off (silent leakage path on a feature explicitly producing shareable artifacts) |
| Filename collisions | Auto-timestamp suffix | Refuse-on-overwrite (forces awkward LLM workflows) or unconditional overwrite (data loss on retry) |
| Path validation rigor | Standard .NET utilities + reserved-name checks | Hardened cross-platform validator (overkill for single-user Windows VM) |
| Timeout strategy | Synchronous, 5-min ceiling | Async job/handle pattern (large feature, unjustified at scope) |

## Open questions

None blocking. Worth revisiting if the deployment model changes (multi-user, exposed beyond localhost, ported off Windows): path validation, timeout strategy, and the obfuscation default would all need fresh treatment.
