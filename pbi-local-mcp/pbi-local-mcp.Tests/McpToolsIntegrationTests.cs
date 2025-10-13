// File: pbi-local-mcp.Tests/McpToolsIntegrationTests.cs
using Microsoft.Extensions.Logging;

using pbi_local_mcp.Core;
using pbi_local_mcp.Tools;

namespace pbi_local_mcp.Tests;

/// <summary>
/// Comprehensive integration tests for all MCP tools.
/// These tests validate connection requirements and ensure tools return complete payloads.
/// </summary>
public class McpToolsIntegrationTests : IDisposable
{
    private readonly ITabularConnection _connection;
    private readonly ILogger<ObjectRetrievalTools> _objectLogger;
    private readonly ILogger<QueryExecutionTools> _queryLogger;
    private readonly ILogger<QueryAnalysisTools> _analysisLogger;
    /// <inheritdoc/>

    public McpToolsIntegrationTests()
    {
        // Setup connection - these tests require an active Power BI instance
        _connection = TestConnectionHelper.CreateConnection();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _objectLogger = loggerFactory.CreateLogger<ObjectRetrievalTools>();
        _queryLogger = loggerFactory.CreateLogger<QueryExecutionTools>();
        _analysisLogger = loggerFactory.CreateLogger<QueryAnalysisTools>();
    }
    /// <inheritdoc/>

    [Fact]
    public async Task ValidateConnection_ShouldSucceed_WhenPowerBIIsConnected()
    {
        // Act & Assert
        await _connection.ValidateConnectionAsync();
    }
    /// <inheritdoc/>

    [Fact]
    public async Task ValidateConnection_ShouldThrow_WhenNoPowerBIInstance()
    {
        // Arrange - create connection to non-existent instance
        var badConnection = new TabularConnection(
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TabularConnection>(),
            "99999", // Invalid port
            "invalid-db-id"
        );

        // Act & Assert
        await Assert.ThrowsAsync<PowerBiConnectionException>(
            async () => await badConnection.ValidateConnectionAsync()
        );
    }
    /// <inheritdoc/>

    #region ObjectRetrievalTools Tests

    [Fact]
    public async Task ListObjects_ShouldReturnNonEmptyPayload_WhenModelLoaded()
    {
        // Arrange
        var tools = new ObjectRetrievalTools(_connection, _objectLogger);

        // Act
        var result = await tools.ListObjects();

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        // Verify structure
        Assert.Contains("\"objects\":", json);
        Assert.Contains("\"totalCount\":", json);
        Assert.Contains("\"filteredCount\":", json);

        // Verify non-zero counts
        dynamic? parsed = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
        Assert.NotNull(parsed);
    }
    /// <inheritdoc/>

    [Fact]
    public async Task ListObjects_Tables_ShouldReturnCompleteMetadata()
    {
        // Arrange
        var tools = new ObjectRetrievalTools(_connection, _objectLogger);

        // Act
        var result = await tools.ListObjects(type: "table");

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);

        var objects = doc.RootElement.GetProperty("objects");
        Assert.True(objects.GetArrayLength() > 0, "Should return at least one table");

        // Verify first table has required metadata
        var firstTable = objects[0];
        Assert.True(firstTable.TryGetProperty("name", out _), "Table should have 'name' property");
        Assert.True(firstTable.TryGetProperty("type", out var typeVal) && typeVal.GetString() == "table");
    }
    /// <inheritdoc/>

    [Fact]
    public async Task ListObjects_Measures_ShouldReturnCompleteMetadata()
    {
        // Arrange
        var tools = new ObjectRetrievalTools(_connection, _objectLogger);

        // Act
        var result = await tools.ListObjects(type: "measure");

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);

        var objects = doc.RootElement.GetProperty("objects");

        if (objects.GetArrayLength() > 0)
        {
            var firstMeasure = objects[0];
            Assert.True(firstMeasure.TryGetProperty("name", out _), "Measure should have 'name' property");
            Assert.True(firstMeasure.TryGetProperty("type", out var typeVal) && typeVal.GetString() == "measure");
            Assert.True(firstMeasure.TryGetProperty("table", out _), "Measure should have 'table' property");
        }
    }
    /// <inheritdoc/>

    [Fact]
    public async Task GetObjectDetails_ShouldThrow_WithoutConnection()
    {
        // Arrange
        var badConnection = new TabularConnection(
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TabularConnection>(),
            "99999",
            "invalid"
        );
        var tools = new ObjectRetrievalTools(badConnection, _objectLogger);

        // Act & Assert
        await Assert.ThrowsAsync<PowerBiConnectionException>(
            async () => await tools.GetObjectDetails("test", "measure")
        );
    }
    /// <inheritdoc/>

    [Fact]
    public async Task ListFunctions_ShouldReturnNonEmptyList()
    {
        // Arrange
        var tools = new ObjectRetrievalTools(_connection, _objectLogger);

        // Act
        var result = await tools.ListFunctions("FILTER");

        // Assert
        Assert.NotNull(result);
        var list = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result);
        var count = list.Cast<object>().Count();
        Assert.True(count > 0, "Should return at least one FILTER function");
    }
    /// <inheritdoc/>

    [Fact]
    public async Task GetFunctionDetails_ShouldReturnCompleteInfo()
    {
        // Arrange
        var tools = new ObjectRetrievalTools(_connection, _objectLogger);

        // Act
        var result = await tools.GetFunctionDetails("CALCULATE");

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("CALCULATE", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FUNCTION_NAME", json, StringComparison.OrdinalIgnoreCase);
    }
    /// <inheritdoc/>

    #endregion

    #region QueryExecutionTools Tests

    [Fact]
    public async Task RunQuery_ShouldExecuteSimpleExpression()
    {
        // Arrange
        var tools = new QueryExecutionTools(_connection, _queryLogger);

        // Act
        var result = await tools.RunQuery("1+1");

        // Assert
        Assert.NotNull(result);
        var list = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result);
        Assert.True(list.Cast<object>().Any(), "Should return at least one row");
    }
    /// <inheritdoc/>

    [Fact]
    public async Task RunQuery_ShouldThrow_WithoutConnection()
    {
        // Arrange
        var badConnection = new TabularConnection(
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TabularConnection>(),
            "99999",
            "invalid"
        );
        var tools = new QueryExecutionTools(badConnection, _queryLogger);

        // Act & Assert
        await Assert.ThrowsAsync<PowerBiConnectionException>(
            async () => await tools.RunQuery("1+1")
        );
    }
    /// <inheritdoc/>

    #endregion

    #region QueryAnalysisTools Tests

    [Fact]
    public async Task ValidateDaxSyntax_ShouldReturnValidationResult()
    {
        // Arrange
        var tools = new QueryAnalysisTools(_connection, _analysisLogger);

        // Act
        var result = await tools.ValidateDaxSyntax("1+1");

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        // Verify structure
        Assert.Contains("\"isValid\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"syntaxErrors\":", json, StringComparison.OrdinalIgnoreCase);

        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("isValid").GetBoolean(), "Simple expression should be valid");
    }
    /// <inheritdoc/>

    [Fact]
    public async Task ValidateDaxSyntax_ShouldThrow_WithoutConnection()
    {
        // Arrange
        var badConnection = new TabularConnection(
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TabularConnection>(),
            "99999",
            "invalid"
        );
        var tools = new QueryAnalysisTools(badConnection, _analysisLogger);

        // Act & Assert
        await Assert.ThrowsAsync<PowerBiConnectionException>(
            async () => await tools.ValidateDaxSyntax("1+1")
        );
    }
    /// <inheritdoc/>

    [Fact]
    public async Task AnalyzeQueryPerformance_ShouldReturnCompleteAnalysis()
    {
        // Arrange
        var tools = new QueryAnalysisTools(_connection, _analysisLogger);

        // Act - Use proper DAX query syntax
        var result = await tools.AnalyzeQueryPerformance("EVALUATE ROW(\"Result\", 1+1)", includeOptimizations: true);

        // Assert
        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        // Verify required sections exist
        Assert.Contains("\"execution\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"performance\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"analysis\":", json, StringComparison.OrdinalIgnoreCase);

        var doc = System.Text.Json.JsonDocument.Parse(json);

        // Verify execution section
        var execution = doc.RootElement.GetProperty("execution");
        Assert.True(execution.TryGetProperty("successful", out var successful));
        Assert.True(successful.GetBoolean(), "Simple query should execute successfully");

        // Verify performance section exists with metrics
        var performance = doc.RootElement.GetProperty("performance");
        Assert.True(performance.TryGetProperty("rating", out _), "Performance should have rating");
        Assert.True(performance.TryGetProperty("metrics", out _), "Performance should have metrics");
    }
    /// <inheritdoc/>

    #endregion

    public void Dispose()
    {
        (_connection as IDisposable)?.Dispose();
    }
}
