using System.Security.Cryptography;
using System.Text;

namespace pbi_local_mcp.Core;

/// <summary>
/// Obfuscation strategy options
/// </summary>
public enum ObfuscationStrategy
{
    /// <summary>No obfuscation</summary>
    None,
    /// <summary>Obfuscate all fields</summary>
    All,
    /// <summary>Obfuscate only text/date fields (dimensions)</summary>
    Dimensions,
    /// <summary>Obfuscate only numeric fields (facts)</summary>
    Facts
}

/// <summary>
/// Field type classification for obfuscation decisions
/// </summary>
public enum FieldType
{
    Text,
    Numeric,
    Date,
    Boolean,
    Unknown
}

/// <summary>
/// Result of obfuscation operation including metadata
/// </summary>
public class ObfuscationResult
{
    /// <summary>
    /// The obfuscated rows
    /// </summary>
    public required List<Dictionary<string, object?>> Rows { get; init; }

    /// <summary>
    /// List of field names that were obfuscated
    /// </summary>
    public required List<string> ObfuscatedFields { get; init; }

    /// <summary>
    /// The strategy used for obfuscation
    /// </summary>
    public required string Strategy { get; init; }

    /// <summary>
    /// Hint for decryption (never includes actual key)
    /// </summary>
    public required string EncryptionHint { get; init; }
}

/// <summary>
/// Service for data obfuscation with encryption
/// </summary>
public class DataObfuscationService : IDisposable
{
    private readonly ObfuscationStrategy _strategy;
    private readonly byte[] _keyBytes;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of DataObfuscationService
    /// </summary>
    /// <param name="strategy">Obfuscation strategy (none, all, dimensions, facts)</param>
    /// <param name="encryptionKey">Encryption key for obfuscation</param>
    public DataObfuscationService(string strategy, string? encryptionKey)
    {
        _strategy = ParseStrategy(strategy);

        if (_strategy != ObfuscationStrategy.None)
        {
            if (string.IsNullOrWhiteSpace(encryptionKey))
                throw new ArgumentException("Encryption key is required when obfuscation is enabled", nameof(encryptionKey));

            if (encryptionKey.Length < 16)
                throw new ArgumentException("Encryption key must be at least 16 characters", nameof(encryptionKey));

            _keyBytes = DeriveKey(encryptionKey);
        }
        else
        {
            _keyBytes = Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Gets the configured obfuscation strategy
    /// </summary>
    public ObfuscationStrategy Strategy => _strategy;

    /// <summary>
    /// Obfuscates data according to the configured strategy
    /// </summary>
    /// <param name="rows">The rows to obfuscate</param>
    /// <param name="columns">The column names</param>
    /// <returns>Obfuscation result with metadata</returns>
    public ObfuscationResult ObfuscateData(
        List<Dictionary<string, object?>> rows,
        List<string> columns)
    {
        if (_strategy == ObfuscationStrategy.None || rows.Count == 0)
        {
            return new ObfuscationResult
            {
                Rows = rows,
                ObfuscatedFields = new List<string>(),
                Strategy = _strategy.ToString(),
                EncryptionHint = "No obfuscation applied"
            };
        }

        // Analyze field types from the data
        var fieldTypes = AnalyzeFieldTypes(rows, columns);

        // Apply strategy-specific obfuscation
        var obfuscatedRows = rows.Select(row => ObfuscateRow(row, fieldTypes)).ToList();

        // Get list of obfuscated fields
        var obfuscatedFields = fieldTypes
            .Where(kvp => ShouldObfuscateField(kvp.Key, kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        return new ObfuscationResult
        {
            Rows = obfuscatedRows,
            ObfuscatedFields = obfuscatedFields,
            Strategy = _strategy.ToString(),
            EncryptionHint = "Use provided encryption key to decrypt values"
        };
    }

    /// <summary>
    /// Analyzes field types from sample data
    /// </summary>
    private Dictionary<string, FieldType> AnalyzeFieldTypes(
        List<Dictionary<string, object?>> rows,
        List<string> columns)
    {
        var fieldTypes = new Dictionary<string, FieldType>();

        foreach (var column in columns)
        {
            // Sample first non-null value to determine type
            var sampleValue = rows
                .Select(r => r.GetValueOrDefault(column))
                .FirstOrDefault(v => v != null);

            if (sampleValue == null)
            {
                fieldTypes[column] = FieldType.Unknown;
                continue;
            }

            fieldTypes[column] = sampleValue switch
            {
                int or long or float or double or decimal => FieldType.Numeric,
                DateTime or DateTimeOffset => FieldType.Date,
                bool => FieldType.Boolean,
                string => FieldType.Text,
                _ => FieldType.Unknown
            };
        }

        return fieldTypes;
    }

    /// <summary>
    /// Obfuscates a single row based on field types
    /// </summary>
    private Dictionary<string, object?> ObfuscateRow(
        Dictionary<string, object?> row,
        Dictionary<string, FieldType> fieldTypes)
    {
        var result = new Dictionary<string, object?>();

        foreach (var kvp in row)
        {
            var fieldName = kvp.Key;
            var value = kvp.Value;

            if (!fieldTypes.TryGetValue(fieldName, out var fieldType))
                fieldType = FieldType.Unknown;

            var shouldObfuscate = ShouldObfuscateField(fieldName, fieldType);

            result[fieldName] = shouldObfuscate && value != null
                ? EncryptValue(value)
                : value;
        }

        return result;
    }

    /// <summary>
    /// Determines if a field should be obfuscated based on strategy and type
    /// </summary>
    private bool ShouldObfuscateField(string fieldName, FieldType type)
    {
        return _strategy switch
        {
            ObfuscationStrategy.All => true,
            ObfuscationStrategy.Dimensions => type == FieldType.Text || type == FieldType.Date,
            ObfuscationStrategy.Facts => type == FieldType.Numeric,
            _ => false
        };
    }

    /// <summary>
    /// Encrypts a value using AES-256 encryption with deterministic output
    /// Uses a deterministic IV derived from the plaintext for consistent encryption
    /// </summary>
    private string EncryptValue(object? value)
    {
        if (value == null)
            return string.Empty;

        var plainText = value.ToString() ?? string.Empty;

        using var aes = Aes.Create();
        aes.Key = _keyBytes;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Derive a deterministic IV from the plaintext
        // This ensures same plaintext always produces same ciphertext
        using var hmac = new HMACSHA256(_keyBytes);
        var ivBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainText));
        aes.IV = ivBytes.Take(16).ToArray(); // AES requires 16-byte IV

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Combine IV and encrypted data for decryption
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        // Return base64 encoded result with prefix to indicate it's encrypted
        return $"ENC:{Convert.ToBase64String(result)}";
    }

    /// <summary>
    /// Decrypts a value that was encrypted with EncryptValue
    /// </summary>
    /// <param name="encryptedValue">The encrypted value (with ENC: prefix)</param>
    /// <returns>The decrypted original value</returns>
    public string DecryptValue(string encryptedValue)
    {
        if (string.IsNullOrWhiteSpace(encryptedValue))
            return string.Empty;

        // Remove ENC: prefix if present
        var base64Value = encryptedValue.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase)
            ? encryptedValue.Substring(4)
            : encryptedValue;

        var fullCipher = Convert.FromBase64String(base64Value);

        using var aes = Aes.Create();
        aes.Key = _keyBytes;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Extract IV (first 16 bytes) and encrypted data
        var iv = new byte[16];
        var cipherText = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        Buffer.BlockCopy(fullCipher, 16, cipherText, 0, cipherText.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// Derives a 256-bit key from the encryption key using PBKDF2
    /// </summary>
    private static byte[] DeriveKey(string encryptionKey)
    {
        // Use fixed salt for deterministic key derivation
        // In production, consider making salt configurable
        var salt = Encoding.UTF8.GetBytes("PBI-MCP-SALT-2024");

        using var deriveBytes = new Rfc2898DeriveBytes(
            encryptionKey,
            salt,
            iterations: 10000,
            HashAlgorithmName.SHA256);

        return deriveBytes.GetBytes(32); // 256-bit key
    }

    /// <summary>
    /// Parses strategy string to enum
    /// </summary>
    private static ObfuscationStrategy ParseStrategy(string strategy)
    {
        return strategy.ToLowerInvariant() switch
        {
            "none" => ObfuscationStrategy.None,
            "all" => ObfuscationStrategy.All,
            "dimensions" => ObfuscationStrategy.Dimensions,
            "facts" => ObfuscationStrategy.Facts,
            _ => throw new ArgumentException($"Invalid obfuscation strategy: {strategy}. Valid values: none, all, dimensions, facts", nameof(strategy))
        };
    }

    /// <summary>
    /// Disposes the service and clears sensitive data
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Clear the key bytes from memory
        if (_keyBytes.Length > 0)
        {
            Array.Clear(_keyBytes, 0, _keyBytes.Length);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}