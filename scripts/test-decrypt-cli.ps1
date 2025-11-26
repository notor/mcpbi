#!/usr/bin/env pwsh
# Test script for DecryptCli tool

$ErrorActionPreference = "Stop"

Write-Host "Testing DecryptCli Tool..." -ForegroundColor Cyan
Write-Host ""

# Test parameters
$testKey = "test-encryption-key-12345"
$testValue = "Hello World"

Write-Host "1. Testing encryption via C# code..." -ForegroundColor Yellow

# Create a simple C# test to encrypt a value
$csharpTest = @"
using pbi_local_mcp.Core;

var service = new DataObfuscationService("all", "$testKey");
var rows = new List<Dictionary<string, object?>> { new() { ["test"] = "$testValue" } };
var columns = new List<string> { "test" };
var result = service.ObfuscateData(rows, columns);
var encrypted = result.Rows[0]["test"]?.ToString() ?? "";
Console.WriteLine(encrypted);
"@

# Save test C# file
$testFile = "temp_encrypt_test.csx"
Set-Content -Path $testFile -Value $csharpTest

# Run encryption using dotnet-script or inline compilation
Write-Host "   Encrypting value: '$testValue'" -ForegroundColor Gray

# Build a small console app to encrypt
$tempProject = "temp_encrypt"
$null = New-Item -ItemType Directory -Force -Path $tempProject
$null = Copy-Item "pbi-local-mcp/pbi-local-mcp.csproj" "$tempProject/"

$programCs = @"
using pbi_local_mcp.Core;

var service = new DataObfuscationService("all", "$testKey");
var rows = new List<Dictionary<string, object?>> { new() { ["test"] = "$testValue" } };
var columns = new List<string> { "test" };
var result = service.ObfuscateData(rows, columns);
var encrypted = result.Rows[0]["test"]?.ToString() ?? "";
Console.WriteLine(encrypted);
"@

Set-Content -Path "$tempProject/Program.cs" -Value $programCs

# Build and run
Write-Host "   Building temporary encryption tool..." -ForegroundColor Gray
$buildOutput = dotnet build "$tempProject" -c Release 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed:" -ForegroundColor Red
    Write-Host $buildOutput
    Remove-Item -Recurse -Force $tempProject -ErrorAction SilentlyContinue
    exit 1
}

$encryptedValue = dotnet run --project "$tempProject" --no-build -c Release 2>&1 | Select-Object -Last 1
Write-Host "   Encrypted value: $encryptedValue" -ForegroundColor Green

# Clean up temp project
Remove-Item -Recurse -Force $tempProject -ErrorAction SilentlyContinue
Remove-Item -Force $testFile -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "2. Testing decryption via DecryptCli..." -ForegroundColor Yellow

# Test decryption
$decryptedValue = dotnet run --project pbi-local-mcp.DecryptCli/pbi-local-mcp.DecryptCli.csproj --no-build -- "$encryptedValue" --key "$testKey" --quiet 2>&1 | Select-Object -Last 1

Write-Host "   Decrypted value: '$decryptedValue'" -ForegroundColor Green

# Verify
Write-Host ""
if ($decryptedValue -eq $testValue) {
    Write-Host "✓ SUCCESS: Encryption/Decryption round-trip successful!" -ForegroundColor Green
    Write-Host "  Original:  '$testValue'" -ForegroundColor Gray
    Write-Host "  Encrypted: '$encryptedValue'" -ForegroundColor Gray
    Write-Host "  Decrypted: '$decryptedValue'" -ForegroundColor Gray
    exit 0
} else {
    Write-Host "✗ FAILURE: Decrypted value doesn't match original!" -ForegroundColor Red
    Write-Host "  Original:  '$testValue'" -ForegroundColor Gray
    Write-Host "  Encrypted: '$encryptedValue'" -ForegroundColor Gray
    Write-Host "  Decrypted: '$decryptedValue'" -ForegroundColor Gray
    exit 1
}