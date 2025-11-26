# Power BI MCP Decryption CLI Tool

A standalone command-line tool for decrypting obfuscated values from the Power BI MCP Server.

## Overview

When the Power BI MCP Server is configured with data obfuscation enabled, query results contain encrypted values prefixed with `ENC:`. This tool allows you to decrypt those values using the same encryption key that was used for obfuscation.

## Prerequisites

- .NET 8.0 SDK or Runtime
- The encryption key used by the MCP server (set via `PBI_ENCRYPTION_KEY` environment variable or `--encryption-key` command line parameter)

## Installation

### Build from Source

```bash
dotnet build pbi-local-mcp.DecryptCli/pbi-local-mcp.DecryptCli.csproj -c Release
```

### Publish as Single Executable

```bash
dotnet publish pbi-local-mcp.DecryptCli/pbi-local-mcp.DecryptCli.csproj -c Release -r win-x64 --self-contained
```

The executable will be in `pbi-local-mcp.DecryptCli/bin/Release/net8.0/win-x64/publish/`

## Usage

### Decrypt a Single Value

```bash
dotnet run --project pbi-local-mcp.DecryptCli -- "ENC:abc123..." --key "your-encryption-key"
```

Or with the published executable:

```bash
pbi-local-mcp.DecryptCli.exe "ENC:abc123..." --key "your-encryption-key"
```

### Decrypt Multiple Values from a File

Create a text file with one encrypted value per line:

```text
ENC:abc123...
ENC:def456...
ENC:ghi789...
```

Then run:

```bash
dotnet run --project pbi-local-mcp.DecryptCli -- --file encrypted-values.txt --key "your-encryption-key"
```

### Save Output to File

```bash
dotnet run --project pbi-local-mcp.DecryptCli -- "ENC:abc123..." --key "your-encryption-key" --output decrypted.txt
```

### Quiet Mode (Only Output Decrypted Values)

```bash
dotnet run --project pbi-local-mcp.DecryptCli -- "ENC:abc123..." --key "your-encryption-key" --quiet
```

## Command Line Options

| Option | Alias | Required | Description |
|--------|-------|----------|-------------|
| `<encrypted-value>` | - | Yes* | The encrypted value to decrypt (with or without `ENC:` prefix) |
| `--key` | `-k` | Yes | The encryption key (minimum 16 characters) |
| `--file` | `-f` | No | Path to file containing encrypted values (one per line) |
| `--output` | `-o` | No | Path to output file (default: stdout) |
| `--quiet` | `-q` | No | Suppress informational messages |

\* Either `<encrypted-value>` or `--file` must be provided

## Examples

### Example 1: Decrypt a Customer Name

```bash
dotnet run --project pbi-local-mcp.DecryptCli -- "ENC:xYz123abc..." --key "my-secret-key-16chars"
```

Output:
```
Decrypting value...

Encrypted:  ENC:xYz123abc...
Decrypted:  John Doe
```

### Example 2: Batch Decrypt with Quiet Mode

Input file (`encrypted-customers.txt`):
```
ENC:xYz123abc...
ENC:pQr456def...
ENC:mNo789ghi...
```

Command:
```bash
dotnet run --project pbi-local-mcp.DecryptCli -- --file encrypted-customers.txt --key "my-secret-key-16chars" --quiet
```

Output:
```
John Doe
Jane Smith
Bob Johnson
```

### Example 3: Decrypt and Save to File

```bash
dotnet run --project pbi-local-mcp.DecryptCli -- --file encrypted-data.txt --key "my-secret-key-16chars" --output decrypted-data.txt
```

## Security Considerations

1. **Keep Your Encryption Key Secure**: The encryption key should be treated as a secret. Don't commit it to source control or share it publicly.

2. **Key Length**: The encryption key must be at least 16 characters long. Longer keys provide better security.

3. **Same Key Required**: You must use the same encryption key that was used to encrypt the data. Using a different key will result in decryption errors.

4. **Encrypted Data Format**: This tool only works with data encrypted by the Power BI MCP Server's DataObfuscationService using AES-256 encryption.

## Error Handling

### Common Errors

**"Encryption key must be at least 16 characters"**
- Solution: Provide a longer encryption key

**"Failed to decrypt value: ... Wrong encryption key"**
- Solution: Verify you're using the correct encryption key that was used to encrypt the data

**"Failed to decrypt value: ... Corrupted encrypted value"**
- Solution: Check that the encrypted value hasn't been modified or truncated

**"File not found"**
- Solution: Verify the file path is correct

## Technical Details

- **Encryption Algorithm**: AES-256 in CBC mode with PKCS7 padding
- **Key Derivation**: PBKDF2 with SHA256 (10,000 iterations)
- **Deterministic Encryption**: Same plaintext always produces same ciphertext (uses HMAC-SHA256 for IV derivation)
- **Format**: `ENC:<base64-encoded-data>` where data includes IV + ciphertext

## Integration with Power BI MCP Server

The MCP server supports the following obfuscation strategies:

- `none` - No obfuscation (default)
- `all` - Obfuscate all fields
- `dimensions` - Obfuscate only text/date fields
- `facts` - Obfuscate only numeric fields

Configure via environment variables or command line:

```bash
# Environment variables
$env:PBI_OBFUSCATION_STRATEGY="all"
$env:PBI_ENCRYPTION_KEY="your-encryption-key-16chars"

# Or via command line
pbi-local-mcp.exe --obfuscation-strategy all --encryption-key "your-encryption-key-16chars"
```

## License

Part of the Power BI MCP Server project.