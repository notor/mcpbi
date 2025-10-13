# Test calculation groups directly
$env:Logging__LogLevel__Default = "Debug"
$env:Logging__LogLevel__pbi_local_mcp = "Debug"

Write-Host "Testing calculation groups..." -ForegroundColor Cyan

dotnet run --project pbi-local-mcp/pbi-local-mcp.csproj -- `
  --connection-string "Provider=MSOLAP;Data Source=localhost:53594;Initial Catalog=3aaa7bef-b9e6-45a6-bfdb-d7b2e3c3e5b2" `
  --verbose