// File: DaxTools.cs
using pbi_local_mcp.Core;

namespace pbi_local_mcp;

/// <summary>
/// DAX utility methods for error handling and message formatting.
/// This class provides static helper methods used across the application.
/// All DAX tool functionality has been moved to specialized tool classes:
/// - QueryExecutionTools: RunQuery, RunQueryAsync
/// - QueryAnalysisTools: ValidateDaxSyntax, AnalyzeQueryPerformance
/// - ObjectRetrievalTools: ListObjects, GetObjectDetails, and all legacy retrieval tools
/// </summary>
public static class DaxTools
{
    /// <summary>
    /// Creates a detailed error message for query execution failures with diagnostics and suggestions
    /// </summary>
    /// <param name="exception">The original exception that occurred</param>
    /// <param name="originalQuery">The original DAX query</param>
    /// <param name="finalQuery">The final processed DAX query</param>
    /// <param name="queryType">The type of query (DAX or DMV)</param>
    /// <returns>Comprehensive error message with diagnostics</returns>
    public static string CreateDetailedErrorMessage(Exception exception, string originalQuery, string finalQuery, QueryType queryType)
    {
        var errorBuilder = new System.Text.StringBuilder();

        errorBuilder.AppendLine("=== DAX QUERY EXECUTION ERROR ===");
        errorBuilder.AppendLine();

        // Error classification
        var errorType = QueryErrorClassifier.ClassifyError(exception);
        errorBuilder.AppendLine($"Error Classification: {errorType}");
        errorBuilder.AppendLine($"Exception Type: {exception.GetType().Name}");
        errorBuilder.AppendLine($"Error Message: {exception.Message}");
        errorBuilder.AppendLine();

        // Query information
        errorBuilder.AppendLine("Query Information:");
        errorBuilder.AppendLine($"   - Query Type: {queryType}");
        errorBuilder.AppendLine($"   - Original Length: {originalQuery.Length} characters");
        errorBuilder.AppendLine($"   - Final Length: {finalQuery.Length} characters");
        errorBuilder.AppendLine($"   - Query Modified: {(originalQuery != finalQuery ? "Yes" : "No")}");
        errorBuilder.AppendLine();

        // Original query
        errorBuilder.AppendLine("Original Query:");
        errorBuilder.AppendLine("+" + "-".PadRight(50, '-') + "+");
        var originalLines = originalQuery.Split('\n');
        foreach (var line in originalLines.Take(10)) // Show first 10 lines
        {
            errorBuilder.AppendLine($"| {line.PadRight(50).Substring(0, Math.Min(50, line.Length))}");
        }
        if (originalLines.Length > 10)
        {
            errorBuilder.AppendLine($"| ... ({originalLines.Length - 10} more lines)");
        }
        errorBuilder.AppendLine("+" + "-".PadRight(50, '-') + "+");
        errorBuilder.AppendLine();

        // Final query (if different)
        if (originalQuery != finalQuery)
        {
            errorBuilder.AppendLine("Processed Query:");
            errorBuilder.AppendLine("+" + "-".PadRight(50, '-') + "+");
            var finalLines = finalQuery.Split('\n');
            foreach (var line in finalLines.Take(10))
            {
                errorBuilder.AppendLine($"| {line.PadRight(50).Substring(0, Math.Min(50, line.Length))}");
            }
            if (finalLines.Length > 10)
            {
                errorBuilder.AppendLine($"| ... ({finalLines.Length - 10} more lines)");
            }
            errorBuilder.AppendLine("+" + "-".PadRight(50, '-') + "+");
            errorBuilder.AppendLine();
        }

        // Suggestions
        var suggestions = QueryErrorClassifier.GetErrorSuggestions(errorType, exception.Message, originalQuery);
        if (suggestions.Any())
        {
            errorBuilder.AppendLine("Troubleshooting Suggestions:");
            foreach (var suggestion in suggestions)
            {
                errorBuilder.AppendLine($"   - {suggestion}");
            }
            errorBuilder.AppendLine();
        }

        // Inner exception details
        if (exception.InnerException != null)
        {
            errorBuilder.AppendLine("Inner Exception Details:");
            errorBuilder.AppendLine($"   Type: {exception.InnerException.GetType().Name}");
            errorBuilder.AppendLine($"   Message: {exception.InnerException.Message}");
            errorBuilder.AppendLine();
        }

        errorBuilder.AppendLine("===================================");

        return errorBuilder.ToString();
    }
}
