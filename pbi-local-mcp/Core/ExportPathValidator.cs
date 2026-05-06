namespace pbi_local_mcp.Core;

/// <summary>
/// Thrown when an LLM-supplied filename fails one of the safety checks in ExportPathValidator.
/// </summary>
public sealed class InvalidFilenameException : Exception
{
    public InvalidFilenameException(string message) : base(message) { }
}

/// <summary>
/// Pure static helper: validates an LLM-supplied filename, appends a timestamp suffix,
/// joins it to the configured export directory, and verifies the resolved path stays inside that directory.
/// No dependencies on other project code — safe to unit-test in isolation.
/// </summary>
public static class ExportPathValidator
{
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Validates <paramref name="filename"/>, appends a timestamp, and returns a canonical full path
    /// that is guaranteed to reside inside <paramref name="exportDir"/>.
    /// Throws <see cref="InvalidFilenameException"/> on any validation failure.
    /// </summary>
    public static string BuildSafePath(string filename, string format, string exportDir)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new InvalidFilenameException("Filename cannot be empty.");

        if (filename.Contains('/') || filename.Contains('\\'))
            throw new InvalidFilenameException("Filename cannot contain path separators.");

        if (filename.Contains(".."))
            throw new InvalidFilenameException("Filename cannot contain '..'.");

        // Absolute path check: drive letter (e.g. C:), or leading slash/backslash
        if (filename.Length >= 2 && char.IsLetter(filename[0]) && filename[1] == ':')
            throw new InvalidFilenameException("Filename cannot be an absolute path.");

        // Colon defense covers alternate-data-stream syntax and drive letters not caught above
        if (filename.Contains(':'))
            throw new InvalidFilenameException("Filename cannot contain ':'.");

        string baseName = Path.GetFileNameWithoutExtension(filename);

        if (string.IsNullOrWhiteSpace(baseName))
            throw new InvalidFilenameException("Filename has no base name.");

        if (WindowsReservedNames.Contains(baseName))
            throw new InvalidFilenameException($"Filename uses a reserved Windows device name: {baseName}.");

        string extension = format.Equals("csv", StringComparison.OrdinalIgnoreCase) ? ".csv" : ".jsonl";
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string finalFilename = $"{baseName}_{timestamp}{extension}";

        string candidate = Path.Combine(exportDir, finalFilename);
        string canonicalCandidate = Path.GetFullPath(candidate);
        string canonicalExportDir = Path.GetFullPath(exportDir);

        // Belt-and-suspenders: verify the resolved path is actually inside the export directory
        string expectedPrefix = canonicalExportDir + Path.DirectorySeparatorChar;
        if (!canonicalCandidate.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidFilenameException("Resolved path escapes the configured export directory.");

        return canonicalCandidate;
    }
}
