namespace pbi_local_mcp.Configuration;

/// <summary>
/// Configuration for the ExportQueryResults tool. Populated from --export-dir and --max-export-rows CLI flags.
/// When ExportDir is null, the export tool is disabled and returns an actionable error.
/// </summary>
public class ExportConfig
{
    /// <summary>
    /// Canonical path to the directory where export files are written.
    /// Null means export is disabled (--export-dir was not supplied).
    /// </summary>
    public string? ExportDir { get; init; }

    /// <summary>
    /// Maximum number of rows allowed per export. Exceeding this fails the export without writing a file.
    /// Default: 100,000.
    /// </summary>
    public int MaxExportRows { get; init; } = 100_000;
}
