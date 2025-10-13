using Microsoft.Extensions.Logging.Abstractions;

namespace pbi_local_mcp.Tests;

/// <summary>
/// Tests for DAX tools error handling scenarios.
/// </summary>
public class DaxToolsErrorHandlingTests
{
    /// <summary>
    /// Tests that a DAX query with an execution error returns a structured error response.
    /// </summary>
    [Fact]
    public async Task RunQuery_DAXError_ReturnsStructuredErrorResponse()
    {
        // Arrange
        var connection = TestConnectionHelper.CreateConnection();
        var toolsLogger = NullLogger<QueryExecutionTools>.Instance;
        var daxTools = new QueryExecutionTools(connection, toolsLogger);

        // Act - Execute query with invalid DAX that will cause execution error
        var result = await daxTools.RunQuery("EVALUATE BADFUNCTION()");

        // Assert - Verify structured error response
        Assert.NotNull(result);
        var resultType = result.GetType();
        var successProperty = resultType.GetProperty("Success");
        Assert.NotNull(successProperty);
        Assert.False((bool)successProperty.GetValue(result)!);

        var errorCategoryProperty = resultType.GetProperty("ErrorCategory");
        Assert.NotNull(errorCategoryProperty);
        Assert.Equal("execution", errorCategoryProperty.GetValue(result));

        var queryInfoProperty = resultType.GetProperty("QueryInfo");
        Assert.NotNull(queryInfoProperty);
        var queryInfo = queryInfoProperty.GetValue(result);
        var originalQueryProperty = queryInfo!.GetType().GetProperty("OriginalQuery");
        Assert.Contains("EVALUATE BADFUNCTION()", originalQueryProperty!.GetValue(queryInfo)!.ToString());
    }

    /// <summary>
    /// Tests that a query with validation errors returns a structured error response.
    /// </summary>
    [Fact]
    public async Task RunQuery_ValidationError_ReturnsStructuredErrorResponse()
    {
        // Arrange
        var connection = TestConnectionHelper.CreateConnection();
        var toolsLogger = NullLogger<QueryExecutionTools>.Instance;
        var daxTools = new QueryExecutionTools(connection, toolsLogger);

        // Act - Test with an empty query that will trigger validation error
        var result = await daxTools.RunQuery("");

        // Assert - Verify structured error response
        Assert.NotNull(result);
        var resultType = result.GetType();
        var successProperty = resultType.GetProperty("Success");
        Assert.NotNull(successProperty);
        Assert.False((bool)successProperty.GetValue(result)!);

        var errorCategoryProperty = resultType.GetProperty("ErrorCategory");
        Assert.NotNull(errorCategoryProperty);
        Assert.Equal("validation", errorCategoryProperty.GetValue(result));

        var errorDetailsProperty = resultType.GetProperty("ErrorDetails");
        Assert.NotNull(errorDetailsProperty);
        var errorDetails = errorDetailsProperty.GetValue(result);
        var messageProperty = errorDetails!.GetType().GetProperty("Message");
        Assert.Contains("DAX query cannot be null or empty", messageProperty!.GetValue(errorDetails)!.ToString());
    }

    /// <summary>
    /// Tests that a query with unbalanced parentheses returns a structured error response.
    /// </summary>
    [Fact]
    public async Task RunQuery_UnbalancedParentheses_ReturnsStructuredErrorResponse()
    {
        // Arrange
        var connection = TestConnectionHelper.CreateConnection();
        var toolsLogger = NullLogger<QueryExecutionTools>.Instance;
        var daxTools = new QueryExecutionTools(connection, toolsLogger);

        // Act - Test with unbalanced parentheses
        var result = await daxTools.RunQuery("SUM(Sales[Amount]");

        // Assert - Verify structured error response
        Assert.NotNull(result);
        var resultType = result.GetType();
        var successProperty = resultType.GetProperty("Success");
        Assert.NotNull(successProperty);
        Assert.False((bool)successProperty.GetValue(result)!);

        var errorCategoryProperty = resultType.GetProperty("ErrorCategory");
        Assert.NotNull(errorCategoryProperty);
        Assert.Equal("validation", errorCategoryProperty.GetValue(result));

        var suggestionsProperty = resultType.GetProperty("Suggestions");
        Assert.NotNull(suggestionsProperty);
        var suggestions = suggestionsProperty.GetValue(result) as System.Collections.IEnumerable;
        Assert.NotNull(suggestions);
        var suggestionsList = suggestions.Cast<string>().ToList();
        Assert.Contains(suggestionsList, s => s.Contains("unbalanced parentheses"));
    }

    /// <summary>
    /// Tests that a query with only whitespace returns a structured error response.
    /// </summary>
    [Fact]
    public async Task RunQuery_WhitespaceOnly_ReturnsStructuredErrorResponse()
    {
        // Arrange
        var connection = TestConnectionHelper.CreateConnection();
        var toolsLogger = NullLogger<QueryExecutionTools>.Instance;
        var daxTools = new QueryExecutionTools(connection, toolsLogger);

        // Act - Test with whitespace-only query
        var result = await daxTools.RunQuery("   \t\n  ");

        // Assert - Verify structured error response
        Assert.NotNull(result);
        var resultType = result.GetType();
        var successProperty = resultType.GetProperty("Success");
        Assert.NotNull(successProperty);
        Assert.False((bool)successProperty.GetValue(result)!);

        var errorCategoryProperty = resultType.GetProperty("ErrorCategory");
        Assert.NotNull(errorCategoryProperty);
        Assert.Equal("validation", errorCategoryProperty.GetValue(result));
    }

    /// <summary>
    /// Tests that an invalid DEFINE query returns a structured error response.
    /// </summary>
    [Fact]
    public async Task RunQuery_InvalidDefineQuery_ReturnsStructuredErrorResponse()
    {
        // Arrange
        var connection = TestConnectionHelper.CreateConnection();
        var toolsLogger = NullLogger<QueryExecutionTools>.Instance;
        var daxTools = new QueryExecutionTools(connection, toolsLogger);

        // Act - Test with invalid DEFINE query structure (missing EVALUATE)
        var result = await daxTools.RunQuery("DEFINE MEASURE Sales[Total] = SUM(Sales[Amount])");

        // Assert - Verify structured error response
        Assert.NotNull(result);
        var resultType = result.GetType();
        var successProperty = resultType.GetProperty("Success");
        Assert.NotNull(successProperty);
        Assert.False((bool)successProperty.GetValue(result)!);

        var errorCategoryProperty = resultType.GetProperty("ErrorCategory");
        Assert.NotNull(errorCategoryProperty);
        Assert.Equal("validation", errorCategoryProperty.GetValue(result));

        var suggestionsProperty = resultType.GetProperty("Suggestions");
        Assert.NotNull(suggestionsProperty);
        var suggestions = suggestionsProperty.GetValue(result) as System.Collections.IEnumerable;
        Assert.NotNull(suggestions);
        var suggestionsList = suggestions.Cast<string>().ToList();
        Assert.Contains(suggestionsList, s => s.Contains("DEFINE blocks must be followed by an EVALUATE statement"));
    }
}
