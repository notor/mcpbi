using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using pbi_local_mcp.Core;
using pbi_local_mcp.Tools;

namespace pbi_local_mcp.Tests;

/// <summary>
/// Unit tests for ObjectRetrievalTools validating input validation and error handling
/// </summary>
public class ObjectRetrievalToolsTests
{
    private readonly Mock<ITabularConnection> _mockConnection;
    private readonly ObjectRetrievalTools _tools;
    private readonly ILogger<ObjectRetrievalTools> _logger;
    /// <inheritdoc/>

    public ObjectRetrievalToolsTests()
    {
        _mockConnection = new Mock<ITabularConnection>();
        _logger = NullLogger<ObjectRetrievalTools>.Instance;
        _tools = new ObjectRetrievalTools(_mockConnection.Object, _logger);
    }
    /// <inheritdoc/>

    #region Input Validation Tests

    [Fact]
    public async Task ListObjects_WithInvalidType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _tools.ListObjects(type: "invalid_type"));
    }
    /// <inheritdoc/>

    [Fact]
    public async Task GetObjectDetails_ByName_RequiresType()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _tools.GetObjectDetails("SomeName"));
    }
    /// <inheritdoc/>


    [Fact]
    public async Task ListFunctions_WithoutInterfaceName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _tools.ListFunctions(""));
    }
    /// <inheritdoc/>

    [Fact]
    public async Task GetFunctionDetails_WithInvalidFunction_ThrowsArgumentException()
    {
        // Arrange
        _mockConnection.Setup(c => c.ExecAsync(
            It.IsAny<string>(),
            QueryType.DMV,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _tools.GetFunctionDetails("NONEXISTENT"));
    }

    #endregion
}
