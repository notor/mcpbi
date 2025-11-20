#!/usr/bin/env pwsh
# Simple test to generate an encrypted value and decrypt it

$ErrorActionPreference = "Stop"

Write-Host "`n=== Simple Encryption/Decryption Test ===`n" -ForegroundColor Cyan

# Configuration
$testKey = "test-encryption-key-12345"
$testValue = "Hello World"

# Step 1: Build projects
Write-Host "Building projects..." -ForegroundColor Yellow
dotnet build pbi-local-mcp/pbi-local-mcp.csproj -c Release -v minimal | Out-Null
dotnet build pbi-local-mcp.DecryptCli/pbi-local-mcp.DecryptCli.csproj -c Release -v minimal | Out-Null
Write-Host "✓ Build complete`n" -ForegroundColor Green

# Step 2: Create a simple program to encrypt
Write-Host "Encrypting value: '$testValue'..." -ForegroundColor Yellow

$tempDir = "temp_encrypt_test"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Create minimal csproj
$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../pbi-local-mcp/pbi-local-mcp.csproj" />
  </ItemGroup>
</Project>
"@

$programCs = @"
using pbi_local_mcp.Core;

var service = new DataObfuscationService("all", "$testKey");
var rows = new List<Dictionary<string, object?>> { 
    new() { ["field"] = "$testValue" } 
};
var result = service.ObfuscateData(rows, new List<string> { "field" });
var encrypted = result.Rows[0]["field"]?.ToString() ?? "";
Console.WriteLine(encrypted);
"@

Set-Content "$tempDir/temp.csproj" $csproj
Set-Content "$tempDir/Program.cs" $programCs

# Build and run
dotnet build $tempDir -c Release -v quiet | Out-Null
$encryptedValue = dotnet run --project $tempDir --no-build -c Release 2>&1 | Where-Object { $_ -like "ENC:*" } | Select-Object -First 1

if (-not $encryptedValue) {
    Write-Host "✗ Failed to generate encrypted value" -ForegroundColor Red
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "✓ Encrypted: $encryptedValue`n" -ForegroundColor Green

# Step 3: Decrypt using CLI
Write-Host "Decrypting using CLI..." -ForegroundColor Yellow
$decryptedValue = dotnet run --project pbi-local-mcp.DecryptCli/pbi-local-mcp.DecryptCli.csproj --no-build -c Release -- "$encryptedValue" --key "$testKey" --quiet 2>&1 | Select-Object -Last 1

Write-Host "✓ Decrypted: $decryptedValue`n" -ForegroundColor Green

# Step 4: Verify
Write-Host "Verification:" -ForegroundColor Yellow
if ($decryptedValue -eq $testValue) {
    Write-Host "✓✓✓ SUCCESS! Encryption/Decryption works perfectly!" -ForegroundColor Green
    Write-Host "`nOriginal:  '$testValue'" -ForegroundColor Gray
    Write-Host "Encrypted: '$encryptedValue'" -ForegroundColor Gray
    Write-Host "Decrypted: '$decryptedValue'" -ForegroundColor Gray
    
    Write-Host "`n" -NoNewline
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host "TO DECRYPT THIS VALUE, USE THIS COMMAND:" -ForegroundColor Yellow
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host "`ndotnet run --project pbi-local-mcp.DecryptCli -- `"$encryptedValue`" --key `"$testKey`" -q`n" -ForegroundColor White
} else {
    Write-Host "✗ FAILED - Values don't match!" -ForegroundColor Red
    Write-Host "Expected: '$testValue'" -ForegroundColor Gray
    Write-Host "Got:      '$decryptedValue'" -ForegroundColor Gray
}

# Cleanup
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue

Write-Host "`n=== Test Complete ===`n" -ForegroundColor Cyan