// File: DaxHelpers.cs
using pbi_local_mcp.Core;

namespace pbi_local_mcp;

/// <summary>
/// DAX syntax validation utilities
/// </summary>
public static class DaxSyntaxValidator
{
    /// <summary>
    /// Normalizes a DAX query by standardizing whitespace and line endings.
    /// </summary>
    public static string NormalizeDAXQuery(string query)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(query, @"\r\n?|\n", "\n");
        normalized = NormalizeWhitespacePreservingStrings(normalized);
        return normalized.Trim();
    }

    /// <summary>
    /// Helper to normalize whitespace while preserving strings.
    /// Collapses multiple whitespace characters into a single space outside of strings.
    /// </summary>
    private static string NormalizeWhitespacePreservingStrings(string input)
    {
        var result = new System.Text.StringBuilder();
        bool inString = false;
        char stringDelimiter = '"';
        bool lastCharWasWhitespace = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (!inString && (c == '"' || c == '\''))
            {
                if (c == '\'' && i + 1 < input.Length && input[i + 1] == '\'')
                {
                }

                if (c == '"')
                {
                    inString = true;
                    stringDelimiter = c;
                    result.Append(c);
                    lastCharWasWhitespace = false;
                    continue;
                }
            }
            else if (inString && c == stringDelimiter)
            {
                if (c == '"' && i + 1 < input.Length && input[i + 1] == '"')
                {
                    result.Append(c);
                    result.Append(input[i + 1]);
                    i++;
                    lastCharWasWhitespace = false;
                    continue;
                }
                inString = false;
                result.Append(c);
                lastCharWasWhitespace = false;
                continue;
            }

            if (inString)
            {
                result.Append(c);
                lastCharWasWhitespace = false;
            }
            else
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastCharWasWhitespace)
                    {
                        result.Append(' ');
                        lastCharWasWhitespace = true;
                    }
                }
                else
                {
                    result.Append(c);
                    lastCharWasWhitespace = false;
                }
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Checks if delimiters like parentheses and brackets are properly balanced.
    /// Skips delimiters found within string literals.
    /// </summary>
    public static void CheckBalancedDelimiters(string query, char openChar, char closeChar, string delimiterName, List<string> errors)
    {
        int balance = 0;
        bool inString = false;
        char stringDelimiter = '\0';

        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];

            if (inString)
            {
                if (c == stringDelimiter)
                {
                    if (i + 1 < query.Length && query[i + 1] == stringDelimiter)
                    {
                        i++;
                    }
                    else
                    {
                        inString = false;
                        stringDelimiter = '\0';
                    }
                }
            }
            else
            {
                if (c == '"' || c == '\'')
                {
                    inString = true;
                    stringDelimiter = c;
                }
                else if (c == openChar)
                {
                    balance++;
                }
                else if (c == closeChar)
                {
                    balance--;
                    if (balance < 0)
                    {
                        errors.Add($"DAX query has unbalanced {delimiterName}: extra '{closeChar}' found.");
                        return;
                    }
                }
            }
        }

        if (balance > 0)
        {
            errors.Add($"DAX query has unbalanced {delimiterName}: {balance} '{openChar}' not closed.");
        }
    }

    /// <summary>
    /// Checks if string delimiters (quotes) are properly balanced.
    /// DAX uses " for string literals and ' for table/column names (which can contain spaces).
    /// Escaped quotes ("" inside strings, '' inside identifiers though less common) are handled.
    /// </summary>
    public static void CheckBalancedQuotes(string query, List<string> errors)
    {
        bool inDoubleQuoteString = false;
        bool inSingleQuoteIdentifier = false;

        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];

            if (c == '"')
            {
                if (inSingleQuoteIdentifier) continue;

                if (i + 1 < query.Length && query[i + 1] == '"')
                {
                    i++;
                }
                else
                {
                    inDoubleQuoteString = !inDoubleQuoteString;
                }
            }
            else if (c == '\'')
            {
                if (inDoubleQuoteString) continue;
                inSingleQuoteIdentifier = !inSingleQuoteIdentifier;
            }
        }

        if (inDoubleQuoteString)
        {
            errors.Add("DAX query has unbalanced double quotes: a string literal is not properly closed.");
        }
        if (inSingleQuoteIdentifier)
        {
            errors.Add("DAX query has unbalanced single quotes: a table/column identifier might not be properly closed.");
        }
    }
}

/// <summary>
/// DAX query analysis utilities
/// </summary>
public static class DaxQueryAnalyzer
{
    /// <summary>
    /// Determines if a DAX expression is a table expression or a scalar expression.
    /// </summary>
    /// <param name="query">The DAX expression to analyze.</param>
    /// <returns>True if the expression is likely a table expression, false if it's a scalar expression.</returns>
    public static bool IsTableExpression(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        query = query.Trim();

        // Check for table reference patterns
        if (query.StartsWith("'") && query.EndsWith("'"))
            return true;

        // Check for common table functions
        var tableExpressionPatterns = new[]
        {
            "SELECTCOLUMNS", "ADDCOLUMNS", "SUMMARIZE", "FILTER", "VALUES", "ALL",
            "DISTINCT", "UNION", "INTERSECT", "EXCEPT", "CROSSJOIN", "NATURALINNERJOIN",
            "NATURALLEFTOUTERJOIN", "TOPN", "SAMPLE", "DATATABLE", "SUBSTITUTEWITHINDEX",
            "GROUPBY", "SUMMARIZECOLUMNS", "TREATAS", "CALCULATETABLE"
        };

        foreach (var pattern in tableExpressionPatterns)
        {
            if (query.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check for calculated table patterns like { ... }
        if (query.StartsWith("{") && query.EndsWith("}"))
            return true;

        return false;
    }
}

/// <summary>
/// Query error classification and suggestion utilities
/// </summary>
public static class QueryErrorClassifier
{
    /// <summary>
    /// Classifies the error based on the exception type and message
    /// </summary>
    /// <param name="exception">The exception to classify</param>
    /// <returns>Error classification string</returns>
    public static string ClassifyError(Exception exception)
    {
        var message = exception.Message.ToLowerInvariant();
        var exceptionType = exception.GetType().Name;

        if (message.Contains("syntax") || message.Contains("parse"))
            return "Syntax Error";
        if (message.Contains("column") && (message.Contains("not found") || message.Contains("doesn't exist")))
            return "Column Reference Error";
        if (message.Contains("table") && (message.Contains("not found") || message.Contains("doesn't exist")))
            return "Table Reference Error";
        if (message.Contains("measure") && (message.Contains("not found") || message.Contains("doesn't exist")))
            return "Measure Reference Error";
        if (message.Contains("function") && (message.Contains("not found") || message.Contains("unknown")))
            return "Function Error";
        if (message.Contains("connection") || message.Contains("timeout"))
            return "Connection Error";
        if (message.Contains("permission") || message.Contains("access"))
            return "Permission Error";
        if (exceptionType.Contains("Argument"))
            return "Parameter Error";
        if (message.Contains("memory") || message.Contains("resource"))
            return "Resource Error";

        return "General Execution Error";
    }

    /// <summary>
    /// Provides specific suggestions based on error type and message
    /// </summary>
    /// <param name="errorType">The classified error type</param>
    /// <param name="errorMessage">The original error message</param>
    /// <param name="query">The query that caused the error</param>
    /// <returns>List of specific suggestions</returns>
    public static List<string> GetErrorSuggestions(string errorType, string errorMessage, string query)
    {
        var suggestions = new List<string>();

        switch (errorType)
        {
            case "Syntax Error":
                suggestions.Add("Check for missing or unmatched parentheses, brackets, or quotes");
                suggestions.Add("Verify that all function names are spelled correctly");
                suggestions.Add("Ensure proper comma placement in function parameters");
                break;

            case "Column Reference Error":
                suggestions.Add("Verify the column name exists in the specified table");
                suggestions.Add("Check if the column name contains special characters that need escaping");
                suggestions.Add("Use the format 'TableName'[ColumnName] for column references");
                break;

            case "Table Reference Error":
                suggestions.Add("Confirm the table name exists in the model");
                suggestions.Add("Check if the table name contains spaces or special characters");
                suggestions.Add("Use single quotes around table names with spaces: 'Table Name'");
                break;

            case "Measure Reference Error":
                suggestions.Add("Verify the measure exists and is accessible");
                suggestions.Add("Check measure name spelling and capitalization");
                suggestions.Add("Ensure the measure is not hidden from client tools");
                break;

            case "Function Error":
                suggestions.Add("Check if the function name is spelled correctly");
                suggestions.Add("Verify the correct number of parameters for the function");
                suggestions.Add("Ensure you're using a supported DAX function");
                break;

            case "Connection Error":
                suggestions.Add("Verify your Power BI Desktop instance is running");
                suggestions.Add("Check that the correct port is being used");
                suggestions.Add("Ensure the tabular model is accessible");
                break;

            case "Permission Error":
                suggestions.Add("Check if you have read access to the data model");
                suggestions.Add("Verify row-level security settings if applicable");
                break;

            case "Parameter Error":
                suggestions.Add("Check that all required parameters are provided");
                suggestions.Add("Verify parameter data types match expected values");
                break;

            case "Resource Error":
                suggestions.Add("Try simplifying the query to reduce memory usage");
                suggestions.Add("Consider breaking complex calculations into smaller parts");
                suggestions.Add("Check if the dataset is too large for the operation");
                break;

            default:
                suggestions.Add("Review the error message for specific details");
                suggestions.Add("Try running a simpler version of the query first");
                suggestions.Add("Check the Power BI Desktop connection status");
                break;
        }

        // Add query-specific suggestions
        if (query.Contains("DEFINE") && !query.Contains("EVALUATE"))
        {
            suggestions.Add("DEFINE blocks must be followed by an EVALUATE statement");
        }

        if (System.Text.RegularExpressions.Regex.Matches(query, @"\(").Count !=
            System.Text.RegularExpressions.Regex.Matches(query, @"\)").Count)
        {
            suggestions.Add("Check for unbalanced parentheses in your query");
        }

        return suggestions;
    }

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
        var errorType = ClassifyError(exception);
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
        var suggestions = GetErrorSuggestions(errorType, exception.Message, originalQuery);
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
