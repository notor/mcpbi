# Claude Code Kickoff Prompt

Drop the two design docs into `docs/` in your fork:

- `docs/EXPORT-DESIGN.md`
- `docs/EXPORT-PSEUDOCODE.md`

Then start a Claude Code session in the repo root and paste the prompt below.

---

## Prompt

I'm adding a new MCP tool, `ExportQueryResults`, to this personal fork of MCPBI. The full design is in `docs/EXPORT-DESIGN.md` and the pseudocode is in `docs/EXPORT-PSEUDOCODE.md`. **Read both documents in full before writing any code.** They contain the architectural decisions, the security posture, and the function-by-function shape of the implementation. Do not relitigate the design — if something looks like a choice, it has already been made and the rationale is in the design doc.

After reading the docs, also read these existing files so you understand the conventions you're matching:

- `pbi-local-mcp/Tools/QueryExecutionTools.cs` (where the new tool method goes; you'll be extracting a shared helper from `RunQuery`)
- `pbi-local-mcp/Resources/Server.cs` (how CLI args, env vars, and DI are wired)
- `pbi-local-mcp/Configuration/CommandLineOptions.cs` and `PowerBiConfig.cs` (config patterns)
- `pbi-local-mcp/Core/DataObfuscationService.cs` (how obfuscation is invoked)
- `pbi-local-mcp/Core/ITabularConnection.cs` (the `ExecAsync` signature)
- `pbi-local-mcp.Tests/SecurityTests.cs` and `RunQueryAsyncTests.cs` (test conventions)

Implement the feature in this order. **Run `dotnet build` after each step and fix any errors before moving on.**

1. **`Configuration/ExportConfig.cs`** — the new config class with `ExportDir` and `MaxExportRows`.

2. **`Core/ExportPathValidator.cs`** — pure static helper, no dependencies on other project code. Easiest to unit-test in isolation.

3. **`Core/ExportWriter.cs`** — `IExportWriter` interface, `JsonlExportWriter` and `CsvExportWriter` implementations, `ExportWriter.For(format)` factory, and the two `JsonConverter` classes (`DBNullJsonConverter`, `DateTimeJsonConverter`). Use `System.Text.Json`.

4. **Refactor `QueryExecutionTools.RunQuery`** to extract the validation + query-construction + execution logic into a private helper `ExecuteDaxAndCollectAsync`. `RunQuery`'s external behavior must not change — verify by re-running existing `RunQuery` tests.

5. **Add the `ExportQueryResults` method** to `QueryExecutionTools.cs`. Inject `ExportConfig` via the constructor (you'll need to update DI registration in `Server.cs`). The method follows the orchestrator pseudocode exactly.

6. **Wire the two new CLI flags** in `Server.cs`'s `ProcessCommandLineArgumentsAsync`: `--export-dir` and `--max-export-rows`. Set env vars `PBI_EXPORT_DIR` and `PBI_MAX_EXPORT_ROWS`. When `--export-dir` is set, resolve to canonical path, create the directory if missing, and verify writability with a temp-file probe before setting the env var. Add the DI registration for `ExportConfig` alongside the existing service registrations.

7. **Tests** — create `pbi-local-mcp.Tests/ExportTests.cs` with the cases enumerated in the pseudocode doc's "Step 6: Tests" section. Tests for `ExportPathValidator` and the writers should be pure unit tests (no Power BI required). The tool-integration tests can use the existing test infrastructure pattern from `RunQueryAsyncTests.cs`. Run `dotnet test` after writing them.

8. **README update** — add a short section near the existing configuration docs explaining the new flags, the opt-in nature of the feature, and a brief usage example. Don't rewrite the README; just add a section.

## Constraints — do not violate

- **Do not change** `ITabularConnection`, `DataObfuscationService`, `DaxSecurityUtils`, `TruncationService`, or any existing tool's external behavior.
- **Do not add new dependencies** beyond what's already in `pbi-local-mcp.csproj`. `System.Text.Json` is already available; no need for Newtonsoft, no need for a CSV library.
- **Do not implement streaming**, multiple concurrent exports, additional formats (Parquet, etc.), or async job patterns. Those are explicit non-goals.
- **Do not weaken** any path validation rule from the pseudocode. If a check looks redundant, it's defense in depth — keep it.
- **Default `applyObfuscation` to `true`** in the tool method signature. This is a deliberate security default.

## When you're done

Confirm:
- `dotnet build` is clean
- `dotnet test` passes (including pre-existing tests)
- The new tool appears in the MCP tools list when the server is run with `--export-dir`
- The new tool returns the "not enabled" error when run without `--export-dir`
- The README section is added
- A summary of changes (files added, files modified) is in your final message

Ask before making any architectural decision the docs don't already settle. Don't expand scope.
