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

        // Original query (truncated to 200 chars)
        var truncatedQuery = originalQuery.Length > 200 ? originalQuery.Substring(0, 200) + "..." : originalQuery;
        errorBuilder.AppendLine($"Query: {truncatedQuery}");
        if (originalQuery != finalQuery)
        {
            errorBuilder.AppendLine($"Query was modified during processing");
        }
        errorBuilder.AppendLine();

        // Top 3 suggestions only
        var suggestions = QueryErrorClassifier.GetErrorSuggestions(errorType, exception.Message, originalQuery);
        if (suggestions.Any())
        {
            errorBuilder.AppendLine("Suggestions: " + string.Join("; ", suggestions.Take(3)));
        }

        errorBuilder.AppendLine("===================================");

        return errorBuilder.ToString();
    }
}
