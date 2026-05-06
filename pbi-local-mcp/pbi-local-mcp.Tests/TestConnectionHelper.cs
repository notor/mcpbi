using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using pbi_local_mcp.Configuration;
using pbi_local_mcp.Core;

namespace pbi_local_mcp.Tests;

/// <summary>
/// Helper class for creating TabularConnection instances in tests with auto-discovery support
/// </summary>
public static class TestConnectionHelper
{
    /// <summary>
    /// Creates a TruncationService for testing
    /// </summary>
    /// <param name="maxRows">Maximum rows (default: 500)</param>
    /// <returns>Configured TruncationService instance</returns>
    public static TruncationService CreateTruncationService(int maxRows = 500)
    {
        return new TruncationService(maxRows);
    }

    /// <summary>
    /// Creates a DataObfuscationService for testing
    /// </summary>
    /// <param name="strategy">Obfuscation strategy (default: "none")</param>
    /// <param name="encryptionKey">Encryption key (required if strategy != "none")</param>
    /// <returns>Configured DataObfuscationService instance</returns>
    public static DataObfuscationService CreateObfuscationService(string strategy = "none", string? encryptionKey = null)
    {
        return new DataObfuscationService(strategy, encryptionKey);
    }

    /// <summary>
    /// Creates an ExportConfig for testing (export disabled by default).
    /// </summary>
    /// <param name="exportDir">Export directory path, or null to disable export.</param>
    /// <param name="maxExportRows">Maximum export rows (default: 100,000).</param>
    public static ExportConfig CreateExportConfig(string? exportDir = null, int maxExportRows = 100_000)
    {
        return new ExportConfig { ExportDir = exportDir, MaxExportRows = maxExportRows };
    }

    /// <summary>
    /// Creates a TabularConnection with auto-discovery if dbId is not provided.
    /// Reads from environment variables and .env file.
    /// </summary>
    /// <param name="logger">Optional logger instance</param>
    /// <param name="useFallback">If true, uses fallback values when connection fails (default: true)</param>
    /// <returns>Configured TabularConnection instance</returns>
    public static TabularConnection CreateConnection(ILogger<TabularConnection>? logger = null, bool useFallback = true)
    {
        logger ??= NullLogger<TabularConnection>.Instance;

        // Read from environment variables
        string? port = Environment.GetEnvironmentVariable("PBI_PORT");
        string? dbId = Environment.GetEnvironmentVariable("PBI_DB_ID");

        // Try to load from .env file if not in environment
        if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(dbId))
        {
            TryLoadEnvFile(ref port, ref dbId);
        }

        // Set defaults if still missing
        port ??= "62678";

        // Try auto-discovery if dbId is missing
        if (string.IsNullOrEmpty(dbId))
        {
            try
            {
                return TabularConnection.CreateWithDiscoveryAsync(port, logger).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (useFallback)
                {
                    Console.WriteLine($"[TestConnectionHelper] Auto-discovery failed: {ex.Message}. Using fallback TestDB.");
                    dbId = "TestDB";
                }
                else
                {
                    throw;
                }
            }
        }

        // Create connection with explicit port and dbId
        var config = new PowerBiConfig { Port = port, DbId = dbId };
        return new TabularConnection(config, logger);
    }

    private static void TryLoadEnvFile(ref string? port, ref string? dbId)
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("Cannot find solution root.");
            }

            string envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        if (string.IsNullOrEmpty(port) && key == "PBI_PORT")
                            port = value;
                        if (string.IsNullOrEmpty(dbId) && key == "PBI_DB_ID")
                            dbId = value;
                    }
                }
            }
        }
        catch
        {
            // Silently ignore .env file loading errors
        }
    }
}
