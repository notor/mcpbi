using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using pbi_local_mcp.Configuration;

namespace pbi_local_mcp.Tests;

/// <summary>
/// Helper class for creating TabularConnection instances in tests with auto-discovery support
/// </summary>
public static class TestConnectionHelper
{
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
