using pbi_local_mcp.Core;
using Xunit;

namespace pbi_local_mcp.Tests;

public class EncryptionDecryptionTests
{
    private const string TestKey = "test-encryption-key-12345";

    [Fact]
    public void EncryptDecrypt_SimpleString_ReturnsOriginalValue()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "Hello World";

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var decrypted = service.DecryptValue(encrypted);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_NumericString_ReturnsOriginalValue()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "12345.67";

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var decrypted = service.DecryptValue(encrypted);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_SpecialCharacters_ReturnsOriginalValue()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "Test!@#$%^&*()_+-={}[]|\\:;\"'<>,.?/";

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var decrypted = service.DecryptValue(encrypted);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_UnicodeCharacters_ReturnsOriginalValue()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "Héllo Wörld 你好世界 🌍";

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var decrypted = service.DecryptValue(encrypted);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_EmptyString_ReturnsEmptyString()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "";

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var decrypted = service.DecryptValue(encrypted);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void Encrypt_SameValue_ProducesSameEncryption()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "Test Value";

        // Act
        var encrypted1 = GetEncryptedValue(service, originalValue);
        var encrypted2 = GetEncryptedValue(service, originalValue);

        // Assert - Deterministic encryption should produce same output
        Assert.Equal(encrypted1, encrypted2);
    }

    [Fact]
    public void DecryptValue_WithoutEncPrefix_StillWorks()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = "Test Value";

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var encryptedWithoutPrefix = encrypted.Substring(4); // Remove "ENC:"
        var decrypted = service.DecryptValue(encryptedWithoutPrefix);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void DecryptValue_WithWrongKey_ThrowsException()
    {
        // Arrange
        using var service1 = new DataObfuscationService("all", TestKey);
        using var service2 = new DataObfuscationService("all", "different-key-67890");
        var originalValue = "Test Value";

        // Act
        var encrypted = GetEncryptedValue(service1, originalValue);

        // Assert
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() =>
        {
            service2.DecryptValue(encrypted);
        });
    }

    [Fact]
    public void EncryptDecrypt_LongString_ReturnsOriginalValue()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var originalValue = new string('A', 10000); // 10KB string

        // Act
        var encrypted = GetEncryptedValue(service, originalValue);
        var decrypted = service.DecryptValue(encrypted);

        // Assert
        Assert.Equal(originalValue, decrypted);
    }

    [Fact]
    public void ObfuscateData_EncryptsAndCanBeDecrypted()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "John Doe", ["Age"] = 30, ["Email"] = "john@example.com" }
        };
        var columns = new List<string> { "Name", "Age", "Email" };

        // Act
        var result = service.ObfuscateData(rows, columns);
        var encryptedRow = result.Rows[0];

        // Decrypt each field
        var decryptedName = service.DecryptValue(encryptedRow["Name"]?.ToString() ?? "");
        var decryptedAge = service.DecryptValue(encryptedRow["Age"]?.ToString() ?? "");
        var decryptedEmail = service.DecryptValue(encryptedRow["Email"]?.ToString() ?? "");

        // Assert
        Assert.Equal("John Doe", decryptedName);
        Assert.Equal("30", decryptedAge);
        Assert.Equal("john@example.com", decryptedEmail);
    }

    [Fact]
    public void ObfuscateData_DimensionsStrategy_OnlyEncryptsTextAndDate()
    {
        // Arrange
        using var service = new DataObfuscationService("dimensions", TestKey);
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "John Doe", ["Age"] = 30, ["Date"] = DateTime.Now }
        };
        var columns = new List<string> { "Name", "Age", "Date" };

        // Act
        var result = service.ObfuscateData(rows, columns);
        var encryptedRow = result.Rows[0];

        // Assert
        Assert.True(encryptedRow["Name"]?.ToString()?.StartsWith("ENC:") ?? false);
        Assert.Equal(30, encryptedRow["Age"]); // Numeric not encrypted
        Assert.True(encryptedRow["Date"]?.ToString()?.StartsWith("ENC:") ?? false);
        Assert.Contains("Name", result.ObfuscatedFields);
        Assert.Contains("Date", result.ObfuscatedFields);
        Assert.DoesNotContain("Age", result.ObfuscatedFields);
    }

    [Fact]
    public void ObfuscateData_FactsStrategy_OnlyEncryptsNumeric()
    {
        // Arrange
        using var service = new DataObfuscationService("facts", TestKey);
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["Name"] = "John Doe", ["Age"] = 30, ["Salary"] = 50000.50 }
        };
        var columns = new List<string> { "Name", "Age", "Salary" };

        // Act
        var result = service.ObfuscateData(rows, columns);
        var encryptedRow = result.Rows[0];

        // Assert
        Assert.Equal("John Doe", encryptedRow["Name"]); // Text not encrypted
        Assert.True(encryptedRow["Age"]?.ToString()?.StartsWith("ENC:") ?? false);
        Assert.True(encryptedRow["Salary"]?.ToString()?.StartsWith("ENC:") ?? false);
        Assert.DoesNotContain("Name", result.ObfuscatedFields);
        Assert.Contains("Age", result.ObfuscatedFields);
        Assert.Contains("Salary", result.ObfuscatedFields);
    }

    [Fact]
    public void DecryptValue_InvalidBase64_ThrowsException()
    {
        // Arrange
        using var service = new DataObfuscationService("all", TestKey);

        // Assert
        Assert.Throws<FormatException>(() =>
        {
            service.DecryptValue("ENC:InvalidBase64!!!!");
        });
    }

    // Helper method to get encrypted value from service
    // This simulates what happens internally during ObfuscateData
    private string GetEncryptedValue(DataObfuscationService service, string value)
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["TestField"] = value }
        };
        var columns = new List<string> { "TestField" };
        var result = service.ObfuscateData(rows, columns);
        return result.Rows[0]["TestField"]?.ToString() ?? "";
    }
}