using pbi_local_mcp.Core;

Console.WriteLine("=== Encryption/Decryption Test ===\n");

var key = "test-encryption-key-12345";
var testValue = "Hello World";

// Create service
using var service = new DataObfuscationService("all", key);

// Encrypt
var rows = new List<Dictionary<string, object?>> { 
    new() { ["TestField"] = testValue } 
};
var result = service.ObfuscateData(rows, new List<string> { "TestField" });
var encrypted = result.Rows[0]["TestField"]?.ToString() ?? "";

Console.WriteLine($"Original:  {testValue}");
Console.WriteLine($"Encrypted: {encrypted}");

// Decrypt
var decrypted = service.DecryptValue(encrypted);
Console.WriteLine($"Decrypted: {decrypted}");

// Verify
if (decrypted == testValue)
{
    Console.WriteLine("\n✓✓✓ SUCCESS! Encryption and decryption work perfectly!");
    Console.WriteLine($"\nTo decrypt this value with the CLI, run:");
    Console.WriteLine($"dotnet run --project pbi-local-mcp.DecryptCli -- \"{encrypted}\" --key \"{key}\" -q");
}
else
{
    Console.WriteLine("\n✗ FAILED! Values don't match");
}