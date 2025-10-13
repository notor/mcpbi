using System.Text.Json;

using Microsoft.Extensions.Logging;

using Xunit.Abstractions;

namespace pbi_local_mcp.Tests;

/// <summary>
/// Comprehensive MCP Server Integration Tests
/// Tests connection, tool discovery, and all tool functionality with fail-fast behavior
/// </summary>
public class McpServerIntegrationTests : IDisposable
{
    private readonly ILogger<TabularConnection> _logger;
    private readonly TabularConnection _connection;
    private readonly ITestOutputHelper _output;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static TestConfig? _testConfig;
    /// <inheritdoc/>

    public McpServerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new TestLogger<TabularConnection>(output);

        // Load test configuration
        if (_testConfig == null)
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
            var configJson = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _testConfig = JsonSerializer.Deserialize<TestConfig>(configJson, options)
                ?? throw new InvalidOperationException("Failed to load test configuration");
        }

        // Create connection with auto-discovery (fail-fast if connection fails)
        try
        {
            _connection = TestConnectionHelper.CreateConnection(_logger, useFallback: false);
            _output.WriteLine($"✓ Connection established to Power BI on port {Environment.GetEnvironmentVariable("PBI_PORT") ?? "58190"}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ FAILED: Cannot connect to Power BI: {ex.Message}");
            throw new InvalidOperationException("Cannot proceed with tests - Power BI connection failed", ex);
        }
    }
    /// <inheritdoc/>

    public void Dispose()
    {
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
    /// <inheritdoc/>

    #region 1. Connection Tests

    [Fact(DisplayName = "1.1 Verify Power BI Connection and Model Loaded")]
    public async Task Test_01_VerifyConnection()
    {
        _output.WriteLine("\n=== TEST 1.1: Verify Connection ===");

        // Test connection validation
        await _connection.ValidateConnectionAsync();
        _output.WriteLine("✓ Connection validated successfully");

        // Verify schema is loaded
        var schema = await _connection.GetSchemaSummaryAsync();
        Assert.True(schema.TableCount > 0, "Expected at least one table in the model");
        Assert.True(schema.MeasureCount >= 0, "Expected measure count to be >= 0");
        Assert.True(schema.ColumnCount > 0, "Expected at least one column in the model");

        _output.WriteLine($"✓ Schema loaded: {schema.TableCount} tables, {schema.MeasureCount} measures, {schema.ColumnCount} columns");
    }
    /// <inheritdoc/>

    [Fact(DisplayName = "1.2 Verify Database Auto-Discovery")]
    public async Task Test_02_VerifyAutoDiscovery()
    {
        _output.WriteLine("\n=== TEST 1.2: Verify Auto-Discovery ===");

        var port = Environment.GetEnvironmentVariable("PBI_PORT") ?? "58190";
        var databases = await TabularConnection.DiscoverDatabasesAsync(port, _logger);

        Assert.NotEmpty(databases);
        _output.WriteLine($"✓ Auto-discovered {databases.Count} database(s): {string.Join(", ", databases)}");
    }
    /// <inheritdoc/>

    #endregion

    #region 2. Tool Discovery Tests

    [Fact(DisplayName = "2.1 Verify All Expected Tools Are Exposed")]
    public void Test_03_VerifyToolsExposed()
    {
        _output.WriteLine("\n=== TEST 2.1: Verify Tools Exposed ===");

        var expectedTools = _testConfig!.ExpectedTools;
        Assert.NotNull(expectedTools);
        Assert.NotEmpty(expectedTools);

        // Verify tools can be instantiated
        var objectTools = new Tools.ObjectRetrievalTools(_connection, new TestLogger<Tools.ObjectRetrievalTools>(_output));
        var queryTools = new QueryExecutionTools(_connection, new TestLogger<QueryExecutionTools>(_output));
        var analysisTools = new QueryAnalysisTools(_connection, new TestLogger<QueryAnalysisTools>(_output));

        Assert.NotNull(objectTools);
        Assert.NotNull(queryTools);
        Assert.NotNull(analysisTools);

        _output.WriteLine($"✓ All tool classes instantiated successfully");
        _output.WriteLine($"✓ Expected {expectedTools.Count} tools: {string.Join(", ", expectedTools)}");
    }
    /// <inheritdoc/>

    #endregion

    #region 3. ListObjects Tool Tests

    [Theory(DisplayName = "3.1 Test ListObjects - All Scenarios")]
    [MemberData(nameof(GetListObjectsScenarios))]
    public async Task Test_04_ListObjects(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 3.1: ListObjects - {scenarioName} ===");

        var tools = new Tools.ObjectRetrievalTools(_connection, new TestLogger<Tools.ObjectRetrievalTools>(_output));

        // Execute tool
        var result = await tools.ListObjects(
            type: GetParam<string>(parameters, "type"),
            tableName: GetParam<string>(parameters, "tableName"),
            isHidden: GetParam<bool?>(parameters, "isHidden"),
            hasDescription: GetParam<bool?>(parameters, "hasDescription"),
            includeBasicDependencies: GetParam<bool>(parameters, "includeBasicDependencies", false)
        );

        Assert.NotNull(result);
        _output.WriteLine($"✓ Tool executed successfully");

        // Validate response structure
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        // Check expected fields
        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }
        _output.WriteLine($"✓ All expected fields present: {string.Join(", ", expectedFields)}");

        // Validate response content
        ValidateResponse(response, validations, _output);

        _output.WriteLine($"✓ All validations passed for scenario: {scenarioName}");
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetListObjectsScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        Console.WriteLine($"[DIAGNOSTIC] Config path: {configPath}");
        Console.WriteLine($"[DIAGNOSTIC] File exists: {File.Exists(configPath)}");

        var configJson = File.ReadAllText(configPath);
        Console.WriteLine($"[DIAGNOSTIC] Config JSON length: {configJson.Length}");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        Console.WriteLine($"[DIAGNOSTIC] Config deserialized: {config != null}");
        Console.WriteLine($"[DIAGNOSTIC] ListObjects is null: {config.ListObjects == null}");
        Console.WriteLine($"[DIAGNOSTIC] ListObjects.Scenarios count: {config.ListObjects?.Scenarios?.Count ?? -1}");

        foreach (var scenario in config.ListObjects.Scenarios)
        {
            Console.WriteLine($"[DIAGNOSTIC] Yielding scenario: {scenario.Name}");
            yield return new object[]
            {
                scenario.Name,
                scenario.Params,
                scenario.ExpectedFields,
                scenario.Validation
            };
        }
    }
    /// <inheritdoc/>

    #endregion

    #region 4. GetObjectDetails Tool Tests

    [Theory(DisplayName = "4.1 Test GetObjectDetails - All Scenarios")]
    [MemberData(nameof(GetObjectDetailsScenarios))]
    public async Task Test_05_GetObjectDetails(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 4.1: GetObjectDetails - {scenarioName} ===");

        var tools = new Tools.ObjectRetrievalTools(_connection, new TestLogger<Tools.ObjectRetrievalTools>(_output));

        // Execute tool
        var result = await tools.GetObjectDetails(
            identifier: GetParam<string>(parameters, "identifier")!,
            type: GetParam<string>(parameters, "type"),
            tableName: GetParam<string>(parameters, "tableName"),
            includeDependencies: GetParam<string>(parameters, "includeDependencies", "none")!,
            includeMetadata: GetParam<bool>(parameters, "includeMetadata", true)
        );

        Assert.NotNull(result);
        _output.WriteLine($"✓ Tool executed successfully");

        // Validate response
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }
        _output.WriteLine($"✓ All expected fields present: {string.Join(", ", expectedFields)}");

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ All validations passed for scenario: {scenarioName}");
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetObjectDetailsScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        var configJson = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;

        foreach (var scenario in config.GetObjectDetails.Scenarios)
        {
            yield return new object[]
            {
                scenario.Name,
                scenario.Params,
                scenario.ExpectedFields,
                scenario.Validation
            };
        }
    }
    /// <inheritdoc/>

    #endregion

    #region 5. PreviewTableData Tool Tests

    [Theory(DisplayName = "5.1 Test PreviewTableData - All Scenarios")]
    [MemberData(nameof(GetPreviewTableDataScenarios))]
    public async Task Test_06_PreviewTableData(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 5.1: PreviewTableData - {scenarioName} ===");

        var tools = new Tools.ObjectRetrievalTools(_connection, new TestLogger<Tools.ObjectRetrievalTools>(_output));

        var result = await tools.PreviewTableData(
            tableName: GetParam<string>(parameters, "tableName")!,
            topN: GetParam<int>(parameters, "topN", 10)
        );

        Assert.NotNull(result);
        _output.WriteLine($"✓ Tool executed successfully");

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }
        _output.WriteLine($"✓ All expected fields present");

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ All validations passed for scenario: {scenarioName}");
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetPreviewTableDataScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData config path: {configPath}");
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData file exists: {File.Exists(configPath)}");
        
        var configJson = File.ReadAllText(configPath);
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData config JSON length: {configJson.Length}");
        
        // Try to extract just the PreviewTableData section
        var jsonDoc = JsonDocument.Parse(configJson);
        if (jsonDoc.RootElement.TryGetProperty("PreviewTableData", out var previewElement))
        {
            Console.WriteLine($"[DIAGNOSTIC] Found PreviewTableData property");
            var previewJson = JsonSerializer.Serialize(previewElement);
            Console.WriteLine($"[DIAGNOSTIC] PreviewTableData JSON: {previewJson.Substring(0, Math.Min(200, previewJson.Length))}...");
            
            if (previewElement.TryGetProperty("scenarios", out var scenariosElement))
            {
                Console.WriteLine($"[DIAGNOSTIC] Found scenarios property, kind: {scenariosElement.ValueKind}, array length: {scenariosElement.GetArrayLength()}");
            }
            else
            {
                Console.WriteLine($"[DIAGNOSTIC] scenarios property NOT FOUND in PreviewTableData");
            }
        }
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData deserialized: {config != null}");
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData is null: {config.PreviewTableData == null}");
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData.Scenarios is null: {config.PreviewTableData.Scenarios == null}");
        Console.WriteLine($"[DIAGNOSTIC] PreviewTableData.Scenarios count: {config.PreviewTableData.Scenarios?.Count ?? 0}");

        // Try deserializing PreviewTableData directly
        if (config.PreviewTableData.Scenarios == null || config.PreviewTableData.Scenarios.Count == 0)
        {
            Console.WriteLine($"[DIAGNOSTIC] Attempting direct deserialization of PreviewTableData section");
            var jsonDoc2 = JsonDocument.Parse(configJson);
            if (jsonDoc2.RootElement.TryGetProperty("PreviewTableData", out var previewElement2))
            {
                var previewGroup = JsonSerializer.Deserialize<ToolTestGroup>(previewElement2.GetRawText(), options);
                if (previewGroup != null && previewGroup.Scenarios != null && previewGroup.Scenarios.Count > 0)
                {
                    Console.WriteLine($"[DIAGNOSTIC] Direct deserialization worked! Found {previewGroup.Scenarios.Count} scenarios");
                    foreach (var scenario in previewGroup.Scenarios)
                    {
                        Console.WriteLine($"[DIAGNOSTIC] Yielding scenario: {scenario.Name}");
                        yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
                    }
                    yield break;
                }
            }
            Console.WriteLine($"[DIAGNOSTIC] No PreviewTableData scenarios found in config!");
            yield break;
        }

        foreach (var scenario in config.PreviewTableData.Scenarios)
        {
            Console.WriteLine($"[DIAGNOSTIC] Yielding PreviewTableData scenario: {scenario.Name}");
            yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
        }
    }
    /// <inheritdoc/>

    #endregion

    #region 6. Function Tools Tests

    [Theory(DisplayName = "6.1 Test ListFunctions - All Scenarios")]
    [MemberData(nameof(GetListFunctionsScenarios))]
    public async Task Test_07_ListFunctions(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 6.1: ListFunctions - {scenarioName} ===");

        var tools = new Tools.ObjectRetrievalTools(_connection, new TestLogger<Tools.ObjectRetrievalTools>(_output));

        var result = await tools.ListFunctions(
            interfaceName: GetParam<string>(parameters, "interfaceName") ?? "Date and Time"
        );

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ Scenario passed: {scenarioName}");
    }
    /// <inheritdoc/>

    [Theory(DisplayName = "6.2 Test GetFunctionDetails - All Scenarios")]
    [MemberData(nameof(GetFunctionDetailsScenarios))]
    public async Task Test_08_GetFunctionDetails(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 6.2: GetFunctionDetails - {scenarioName} ===");

        var tools = new Tools.ObjectRetrievalTools(_connection, new TestLogger<Tools.ObjectRetrievalTools>(_output));

        var result = await tools.GetFunctionDetails(
            functionName: GetParam<string>(parameters, "functionName")!
        );

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        
        // GetFunctionDetails can return either a single dictionary or a list
        Dictionary<string, JsonElement>? response = null;
        try
        {
            response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException)
        {
            // If it's a list, get the first item
            var list = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            response = list?.FirstOrDefault();
        }
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ Scenario passed: {scenarioName}");
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetListFunctionsScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        var configJson = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        foreach (var scenario in config.ListFunctions.Scenarios)
            yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetFunctionDetailsScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        var configJson = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        foreach (var scenario in config.GetFunctionDetails.Scenarios)
            yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
    }
    /// <inheritdoc/>

    #endregion

    #region 7. Query Execution Tests

    [Theory(DisplayName = "7.1 Test RunQuery - All Scenarios")]
    [MemberData(nameof(GetRunQueryScenarios))]
    public async Task Test_09_RunQuery(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 7.1: RunQuery - {scenarioName} ===");

        var tools = new QueryExecutionTools(_connection, new TestLogger<QueryExecutionTools>(_output));

        var result = await tools.RunQuery(
            dax: GetParam<string>(parameters, "dax")!,
            topN: GetParam<int>(parameters, "topN", 10)
        );

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ Scenario passed: {scenarioName}");
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetRunQueryScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        var configJson = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        foreach (var scenario in config.RunQuery.Scenarios)
            yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
    }
    /// <inheritdoc/>

    #endregion

    #region 8. Query Analysis Tests

    [Theory(DisplayName = "8.1 Test ValidateDaxSyntax - All Scenarios")]
    [MemberData(nameof(GetValidateDaxScenarios))]
    public async Task Test_10_ValidateDaxSyntax(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 8.1: ValidateDaxSyntax - {scenarioName} ===");

        var tools = new QueryAnalysisTools(_connection, new TestLogger<QueryAnalysisTools>(_output));

        var result = await tools.ValidateDaxSyntax(
            daxExpression: GetParam<string>(parameters, "daxExpression")!,
            includeRecommendations: GetParam<bool>(parameters, "includeRecommendations", true)
        );

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ Scenario passed: {scenarioName}");
    }
    /// <inheritdoc/>

    [Theory(DisplayName = "8.2 Test AnalyzeQueryPerformance - All Scenarios")]
    [MemberData(nameof(GetAnalyzePerformanceScenarios))]
    public async Task Test_11_AnalyzeQueryPerformance(string scenarioName, Dictionary<string, object?> parameters, List<string> expectedFields, Dictionary<string, JsonElement> validations)
    {
        _output.WriteLine($"\n=== TEST 8.2: AnalyzeQueryPerformance - {scenarioName} ===");

        var tools = new QueryAnalysisTools(_connection, new TestLogger<QueryAnalysisTools>(_output));

        var result = await tools.AnalyzeQueryPerformance(
            daxQuery: GetParam<string>(parameters, "daxQuery")!,
            includeOptimizations: GetParam<bool>(parameters, "includeOptimizations", true)
        );

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var response = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        Assert.NotNull(response);

        foreach (var field in expectedFields)
        {
            Assert.True(response.ContainsKey(field), $"Response missing expected field: {field}");
        }

        ValidateResponse(response, validations, _output);
        _output.WriteLine($"✓ Scenario passed: {scenarioName}");
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetValidateDaxScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        var configJson = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        foreach (var scenario in config.ValidateDaxSyntax.Scenarios)
            yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
    }
    /// <inheritdoc/>

    public static IEnumerable<object[]> GetAnalyzePerformanceScenarios()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tooltest.config.json");
        var configJson = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<TestConfig>(configJson, options)!;
        foreach (var scenario in config.AnalyzeQueryPerformance.Scenarios)
            yield return new object[] { scenario.Name, scenario.Params, scenario.ExpectedFields, scenario.Validation };
    }

    #endregion

    #region Helper Methods

    private static T? GetParam<T>(Dictionary<string, object?> parameters, string key, T? defaultValue = default)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is JsonElement jsonElement)
        {
            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    private static void ValidateResponse(Dictionary<string, JsonElement> response, Dictionary<string, JsonElement> validations, ITestOutputHelper output)
    {
        foreach (var (path, expectedElement) in validations)
        {
            var value = GetJsonValue(response, path);

            // Convert JsonElement to string for comparison
            string expected;
            if (expectedElement.ValueKind == JsonValueKind.String)
            {
                expected = expectedElement.GetString()!;
            }
            else if (expectedElement.ValueKind == JsonValueKind.True)
            {
                expected = "true";
            }
            else if (expectedElement.ValueKind == JsonValueKind.False)
            {
                expected = "false";
            }
            else
            {
                expected = expectedElement.GetRawText();
            }

            if (expected == "exists")
            {
                Assert.True(value != null, $"Expected '{path}' to exist in response");
                output.WriteLine($"  ✓ {path}: exists");
            }
            else if (expected == "array")
            {
                Assert.True(value?.ValueKind == JsonValueKind.Array, $"Expected '{path}' to be an array");
                output.WriteLine($"  ✓ {path}: is array");
            }
            else if (expected.StartsWith(">= "))
            {
                var threshold = int.Parse(expected.Substring(3));
                var actualValue = value?.GetInt32() ?? 0;
                Assert.True(actualValue >= threshold, $"Expected '{path}' >= {threshold}, got {actualValue}");
                output.WriteLine($"  ✓ {path}: {actualValue} >= {threshold}");
            }
            else if (expected.StartsWith("> "))
            {
                var threshold = int.Parse(expected.Substring(2));
                var actualValue = value?.GetInt32() ?? 0;
                Assert.True(actualValue > threshold, $"Expected '{path}' > {threshold}, got {actualValue}");
                output.WriteLine($"  ✓ {path}: {actualValue} > {threshold}");
            }
            else if (expected.StartsWith("<= "))
            {
                var threshold = int.Parse(expected.Substring(3));
                var actualValue = value?.GetInt32() ?? 0;
                Assert.True(actualValue <= threshold, $"Expected '{path}' <= {threshold}, got {actualValue}");
                output.WriteLine($"  ✓ {path}: {actualValue} <= {threshold}");
            }
            else if (expected == "true")
            {
                Assert.True(value?.GetBoolean() == true, $"Expected '{path}' to be true");
                output.WriteLine($"  ✓ {path}: true");
            }
            else if (expected == "false")
            {
                Assert.True(value?.GetBoolean() == false, $"Expected '{path}' to be false");
                output.WriteLine($"  ✓ {path}: false");
            }
            else
            {
                var actualValue = value?.GetString();
                Assert.Equal(expected, actualValue);
                output.WriteLine($"  ✓ {path}: {actualValue}");
            }
        }
    }

    private static JsonElement? GetJsonValue(Dictionary<string, JsonElement> obj, string path)
    {
        var parts = path.Split('.');
        JsonElement? current = null;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            
            // Special handling for "length" property on arrays
            if (part == "length" && current.HasValue && current.Value.ValueKind == JsonValueKind.Array)
            {
                // Return array length as a JsonElement
                var length = current.Value.GetArrayLength();
                var doc = JsonDocument.Parse(length.ToString());
                return doc.RootElement.Clone();
            }
            
            if (current == null)
            {
                // Handle root-level property with or without array index
                if (part.Contains("["))
                {
                    var arrayName = part.Substring(0, part.IndexOf('['));
                    var index = int.Parse(part.Substring(part.IndexOf('[') + 1, part.IndexOf(']') - part.IndexOf('[') - 1));

                    if (!obj.TryGetValue(arrayName, out var arrayElem))
                        return null;

                    if (arrayElem.ValueKind != JsonValueKind.Array || arrayElem.GetArrayLength() <= index)
                        return null;

                    current = arrayElem[index];
                }
                else
                {
                    if (!obj.TryGetValue(part, out var elem))
                        return null;
                    current = elem;
                }
            }
            else
            {
                if (part.Contains("["))
                {
                    var arrayName = part.Substring(0, part.IndexOf('['));
                    var index = int.Parse(part.Substring(part.IndexOf('[') + 1, part.IndexOf(']') - part.IndexOf('[') - 1));

                    if (!current.Value.TryGetProperty(arrayName, out var arrayElem))
                        return null;

                    if (arrayElem.ValueKind != JsonValueKind.Array || arrayElem.GetArrayLength() <= index)
                        return null;

                    current = arrayElem[index];
                }
                else
                {
                    if (!current.Value.TryGetProperty(part, out var elem))
                        return null;
                    current = elem;
                }
            }
        }

        return current;
    }

    #endregion

    #region Test Configuration Classes

    private class TestConfig
    {
        public List<string> ExpectedTools { get; set; } = new();
        public ToolTestGroup ListObjects { get; set; } = new();
        public ToolTestGroup GetObjectDetails { get; set; } = new();
        public ToolTestGroup PreviewTableData { get; set; } = new();
        public ToolTestGroup ListFunctions { get; set; } = new();
        public ToolTestGroup GetFunctionDetails { get; set; } = new();
        public ToolTestGroup RunQuery { get; set; } = new();
        public ToolTestGroup ValidateDaxSyntax { get; set; } = new();
        public ToolTestGroup AnalyzeQueryPerformance { get; set; } = new();
    }

    private class ToolTestGroup
    {
        public List<TestScenario> Scenarios { get; set; } = new();
    }

    private class TestScenario
    {
        public string Name { get; set; } = "";
        public Dictionary<string, object?> Params { get; set; } = new();
        public List<string> ExpectedFields { get; set; } = new();
        public Dictionary<string, JsonElement> Validation { get; set; } = new();
    }

    #endregion
}

/// <summary>
/// Test logger that writes to xUnit test output
/// </summary>
internal class TestLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _output;

    public TestLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception != null)
            message += $"\n{exception}";
        _output.WriteLine($"[{logLevel}] {message}");
    }
}
