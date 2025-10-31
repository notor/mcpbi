namespace pbi_local_mcp.Core;

/// <summary>
/// Result of truncation operation including metadata
/// </summary>
public class TruncationResult<T>
{
    /// <summary>
    /// The truncated data
    /// </summary>
    public required List<T> Data { get; init; }

    /// <summary>
    /// Total number of rows before truncation
    /// </summary>
    public required int TotalRows { get; init; }

    /// <summary>
    /// Number of rows in the truncated result
    /// </summary>
    public required int DisplayedRows { get; init; }

    /// <summary>
    /// Whether the data was truncated
    /// </summary>
    public required bool Truncated { get; init; }

    /// <summary>
    /// Whether there are more rows available
    /// </summary>
    public required bool HasMore { get; init; }
}

/// <summary>
/// Service for applying row limits and truncation to query results
/// </summary>
public class TruncationService
{
    private readonly int _maxRows;

    /// <summary>
    /// Initializes a new instance of TruncationService
    /// </summary>
    /// <param name="maxRows">Maximum number of rows to return (default: 500)</param>
    public TruncationService(int maxRows = 500)
    {
        if (maxRows <= 0)
            throw new ArgumentException("MaxRows must be greater than 0", nameof(maxRows));

        _maxRows = maxRows;
    }

    /// <summary>
    /// Gets the configured maximum rows
    /// </summary>
    public int MaxRows => _maxRows;

    /// <summary>
    /// Applies truncation to a collection of data
    /// </summary>
    /// <typeparam name="T">Type of data items</typeparam>
    /// <param name="data">The data to truncate</param>
    /// <param name="requestedLimit">Optional requested limit (will be capped at MaxRows)</param>
    /// <returns>Truncation result with metadata</returns>
    public TruncationResult<T> ApplyTruncation<T>(
        IEnumerable<T> data,
        int? requestedLimit = null)
    {
        var dataList = data.ToList();
        var totalRows = dataList.Count;

        // Effective limit is the minimum of requested limit and max rows
        var effectiveLimit = requestedLimit.HasValue
            ? Math.Min(requestedLimit.Value, _maxRows)
            : _maxRows;

        var truncatedData = dataList.Take(effectiveLimit).ToList();

        return new TruncationResult<T>
        {
            Data = truncatedData,
            TotalRows = totalRows,
            DisplayedRows = truncatedData.Count,
            Truncated = totalRows > truncatedData.Count,
            HasMore = totalRows > truncatedData.Count
        };
    }

    /// <summary>
    /// Applies truncation and returns dictionary-based result with metadata
    /// </summary>
    /// <param name="rows">The row data</param>
    /// <param name="columns">The column names</param>
    /// <param name="requestedLimit">Optional requested limit</param>
    /// <param name="executionTime">Optional execution time in milliseconds</param>
    /// <returns>Dictionary containing rows, columns, and truncation metadata</returns>
    public Dictionary<string, object?> ApplyTruncationWithMetadata(
        List<Dictionary<string, object?>> rows,
        List<string> columns,
        int? requestedLimit = null,
        double? executionTime = null)
    {
        var result = ApplyTruncation(rows, requestedLimit);

        var response = new Dictionary<string, object?>
        {
            ["rows"] = result.Data,
            ["columns"] = columns,
            ["rowCount"] = result.DisplayedRows, // Backward compatibility
            ["totalRows"] = result.TotalRows,
            ["displayedRows"] = result.DisplayedRows,
            ["truncated"] = result.Truncated,
            ["hasMore"] = result.HasMore
        };

        if (executionTime.HasValue)
        {
            response["executionTime"] = Math.Round(executionTime.Value, 2);
        }

        return response;
    }
}