# Open issues

## TD-001 — RunQueryAsync logic is duplicated

`QueryExecutionTools.RunQueryAsync` contains its own inline copies of the validation and
query-construction logic that was extracted into `ExecuteDaxAndCollectAsync` during the
export refactor. `RunQueryAsync` predates `RunQuery` and has different return semantics
(returns a JSON string, supports DMV, has a verbose envelope mode), so it was left out of
scope. The duplication is not a regression but is technical debt.

**What to do:** Refactor `RunQueryAsync` to call `ExecuteDaxAndCollectAsync` for the DAX
path, handling the DMV path separately. Verify all `RunQueryAsyncTests` still pass.

---

## TD-002 — ExportQueryResults obfuscation not end-to-end tested

`ExportQueryResults_ObfuscationDefaultIsOn_FlagReflectsStrategy` only verifies the receipt
fields (`obfuscated`, `obfuscationStrategy`) when the server is configured with
`strategy = none`. It does not assert that data values in the exported file were actually
altered when a real obfuscation strategy (e.g. `dimensions`) is active.

**What to do:** Add a test that configures `DataObfuscationService` with `strategy =
"dimensions"`, runs `ExportQueryResults` against a query returning known string values,
reads the output file, and asserts the values differ from the originals. Requires PBI or a
mock `ITabularConnection`.
