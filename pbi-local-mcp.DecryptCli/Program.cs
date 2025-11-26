using System.CommandLine;
using pbi_local_mcp.Core;

namespace pbi_local_mcp.DecryptCli;

/// <summary>
/// CLI tool for decrypting obfuscated values from Power BI MCP Server
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Power BI MCP Decryption Tool - Decrypt obfuscated values using your encryption key");

        var encryptedValueArgument = new Argument<string>(
            name: "encrypted-value",
            description: "The encrypted value to decrypt (with or without ENC: prefix)");

        var encryptionKeyOption = new Option<string>(
            name: "--key",
            description: "The encryption key used for obfuscation (minimum 16 characters)")
        {
            IsRequired = true
        };
        encryptionKeyOption.AddAlias("-k");

        var batchFileOption = new Option<FileInfo?>(
            name: "--file",
            description: "Path to a file containing encrypted values (one per line)")
        {
            IsRequired = false
        };
        batchFileOption.AddAlias("-f");

        var outputFileOption = new Option<FileInfo?>(
            name: "--output",
            description: "Path to output file for decrypted values (default: stdout)")
        {
            IsRequired = false
        };
        outputFileOption.AddAlias("-o");

        var quietOption = new Option<bool>(
            name: "--quiet",
            description: "Suppress informational messages, only output decrypted values",
            getDefaultValue: () => false);
        quietOption.AddAlias("-q");

        rootCommand.Add(encryptedValueArgument);
        rootCommand.Add(encryptionKeyOption);
        rootCommand.Add(batchFileOption);
        rootCommand.Add(outputFileOption);
        rootCommand.Add(quietOption);

        rootCommand.SetHandler(async (string encryptedValue, string encryptionKey, FileInfo? batchFile, FileInfo? outputFile, bool quiet) =>
        {
            try
            {
                // Validate encryption key
                if (string.IsNullOrWhiteSpace(encryptionKey))
                {
                    Console.Error.WriteLine("Error: Encryption key cannot be empty");
                    Environment.Exit(1);
                }

                if (encryptionKey.Length < 16)
                {
                    Console.Error.WriteLine("Error: Encryption key must be at least 16 characters");
                    Environment.Exit(1);
                }

                // Initialize decryption service
                using var obfuscationService = new DataObfuscationService("all", encryptionKey);

                TextWriter output = Console.Out;
                if (outputFile != null)
                {
                    output = new StreamWriter(outputFile.FullName);
                }

                try
                {
                    if (batchFile != null)
                    {
                        // Batch mode: decrypt values from file
                        await DecryptBatchFile(obfuscationService, batchFile, output, quiet);
                    }
                    else
                    {
                        // Single value mode
                        await DecryptSingleValue(obfuscationService, encryptedValue, output, quiet);
                    }
                }
                finally
                {
                    if (output != Console.Out)
                    {
                        await output.DisposeAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (!quiet)
                {
                    Console.Error.WriteLine($"Details: {ex.GetType().Name}");
                    Console.Error.WriteLine(ex.StackTrace);
                }
                Environment.Exit(1);
            }
        }, encryptedValueArgument, encryptionKeyOption, batchFileOption, outputFileOption, quietOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task DecryptSingleValue(
        DataObfuscationService service,
        string encryptedValue,
        TextWriter output,
        bool quiet)
    {
        if (string.IsNullOrWhiteSpace(encryptedValue))
        {
            Console.Error.WriteLine("Error: Encrypted value cannot be empty");
            Environment.Exit(1);
        }

        if (!quiet)
        {
            await output.WriteLineAsync("Decrypting value...");
            await output.WriteLineAsync();
        }

        try
        {
            var decryptedValue = service.DecryptValue(encryptedValue);

            if (!quiet)
            {
                await output.WriteLineAsync("Encrypted:  " + encryptedValue);
                await output.WriteLineAsync("Decrypted:  " + decryptedValue);
            }
            else
            {
                await output.WriteLineAsync(decryptedValue);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to decrypt value: {ex.Message}");
            Console.Error.WriteLine("Possible reasons:");
            Console.Error.WriteLine("  - Wrong encryption key");
            Console.Error.WriteLine("  - Corrupted encrypted value");
            Console.Error.WriteLine("  - Value was not encrypted with this tool");
            throw;
        }
    }

    private static async Task DecryptBatchFile(
        DataObfuscationService service,
        FileInfo inputFile,
        TextWriter output,
        bool quiet)
    {
        if (!inputFile.Exists)
        {
            Console.Error.WriteLine($"Error: File not found: {inputFile.FullName}");
            Environment.Exit(1);
        }

        if (!quiet)
        {
            await output.WriteLineAsync($"Processing batch file: {inputFile.FullName}");
            await output.WriteLineAsync();
        }

        var lines = await File.ReadAllLinesAsync(inputFile.FullName);
        var successCount = 0;
        var errorCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var decryptedValue = service.DecryptValue(line.Trim());
                
                if (!quiet)
                {
                    await output.WriteLineAsync($"✓ {line.Trim()} => {decryptedValue}");
                }
                else
                {
                    await output.WriteLineAsync(decryptedValue);
                }
                
                successCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                if (!quiet)
                {
                    await output.WriteLineAsync($"✗ {line.Trim()} => ERROR: {ex.Message}");
                }
                else
                {
                    Console.Error.WriteLine($"Error decrypting line: {line.Trim()}");
                }
            }
        }

        if (!quiet)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"Summary: {successCount} succeeded, {errorCount} failed");
        }
    }
}