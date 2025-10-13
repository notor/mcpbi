// File: Core/PowerBiConnectionException.cs
namespace pbi_local_mcp.Core;

/// <summary>
/// Exception thrown when Power BI connection is not available or invalid
/// </summary>
public class PowerBiConnectionException : Exception
{
    public PowerBiConnectionException()
        : base("No Power BI Desktop instance is connected. Please open a Power BI file (.pbix) in Power BI Desktop and try again.")
    {
    }

    public PowerBiConnectionException(string message) : base(message)
    {
    }

    public PowerBiConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
