namespace pbi_local_mcp.Configuration;

/// <summary>
/// Configuration options for command-line arguments
/// </summary>
public class CommandLineOptions
{
    /// <summary>
    /// Gets or sets the PowerBI port number from command-line argument
    /// </summary>
    public string? Port { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of rows to return in query results (default: 500)
    /// </summary>
    public int MaxRows { get; set; } = 500;

    /// <summary>
    /// Gets or sets the data obfuscation strategy: none (default), all, dimensions, or facts
    /// </summary>
    public string ObfuscationStrategy { get; set; } = "none";

    /// <summary>
    /// Gets or sets the encryption key for data obfuscation (required if ObfuscationStrategy != none)
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Validates the command-line options
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Port))
            return false;

        if (!int.TryParse(Port, out var port) || port < 1 || port > 65535)
            return false;

        if (MaxRows <= 0)
            return false;

        var validStrategies = new[] { "none", "all", "dimensions", "facts" };
        if (!validStrategies.Contains(ObfuscationStrategy.ToLowerInvariant()))
            return false;

        // If obfuscation is enabled, encryption key is required
        if (ObfuscationStrategy.ToLowerInvariant() != "none")
        {
            if (string.IsNullOrWhiteSpace(EncryptionKey))
                return false;

            // Validate minimum key strength (at least 16 characters)
            if (EncryptionKey.Length < 16)
                return false;
        }

        return true;
    }
}
