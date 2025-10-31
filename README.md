# MCPBI - Tabular Model MCP Server
This is a Model Context Protocol (MCP) server for locally running Tabular Models, i.e. PowerBI models running on PowerBI Desktop. 

This server allows MCP-enabled LLM clients to communicate with your tabular models and help you debug, analyse and compose DAX queries. 

*Example: Copilot querying Tabular Model via MCP*

# How it works 

It connects to a local running instance of Tabular models using the [AdomdConnection in ADOMD.NET](https://learn.microsoft.com/en-us/analysis-services/adomd/multidimensional-models-adomd-net-client/connections-in-adomd-net?view=asallproducts-allversions). 

Using this connection, the server then allows clients to execute [DAX-queries](https://www.sqlbi.com/articles/execute-dax-queries-through-ole-db-and-adomd-net/) and retrieve model metadata (using DMV queries) through pre-defined tools for high accuracy, as well as custom DAX queries for debugging and development.

## Features

## Configuration Options

### Port
Specify the Power BI Desktop instance port to connect to (required).

```bash
# Example: Connect to Power BI Desktop instance on port 56751
dotnet run -- --port 56751
```

### Obfuscation
Protect sensitive data with configurable encryption strategies:
- **None** (default): No obfuscation
- **All**: Mask all values
- **Dimensions**: Mask only text/date fields (keep numeric data visible)
- **Facts**: Mask only numeric fields (keep dimensional context)

```bash
# Example: Protect customer/product names while keeping sales figures visible
dotnet run -- --port 56751 --obfuscation-strategy dimensions --encryption-key "YourSecureKey123!"
```

### Output Truncation
Automatic row limiting with comprehensive metadata:
- Default 500-row limit (configurable)
- Response includes: `totalRows`, `displayedRows`, `truncated`, `hasMore`

```bash
# Example: Increase row limit for analysis
dotnet run -- --port 56751 --max-rows 2000
```

## Tools

#### list_objects
- Lists all objects in the model (tables, columns, measures, relationships)
- Returns object type, name, and basic metadata

#### get_object_details
- Retrieves detailed metadata for a specific object (table, column, measure)
- Returns properties, data types, relationships, and dependencies

#### list_functions
- Lists available DAX functions with optional filtering by category or origin
- Returns function name, description, interface classification, and origin

#### get_function_details
- Retrieves comprehensive details for a specific DAX function including full parameter information
- Returns function signature, parameter requirements, return type, and DirectQuery compatibility

#### run_query
- Executes a custom DAX query against the model
- Returns query results with metadata (total rows, truncated status)

#### validate_query
- Validates DAX query syntax without execution
- Returns syntax correctness and error messages if applicable

#### analyze_query_performance
- Analyzes DAX query performance and provides optimization suggestions
- Returns execution time, resource usage, and improvement recommendations


## Installation
# Setup Instructions

## Requirements

- Power BI Desktop (with a PBIX file open for discovery)
- Windows OS
- Visual Studio Code (for MCP server integration)
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Setup from Prebuilt Release

1. **Download the release** from the [Releases](releases/) directory or GitHub releases page and extract to your preferred location.

2. **Open Power BI Desktop** with a PBIX file you want to work with.

3. **Get PowerBI instance information** 

There are several ways to detect Power BI Desktop instances.
The easiest is to open Tabular Editor and check the port in the connection string.

![Tabular Editor](image.png)
   
Simply add the port to your MCP server configuration in the next step (you can ignore the database ID, as this server connects to the default model).   
If you don't have Tabular Editor, you can use the included discovery tool to find the running instance and database.

   Open PowerShell, navigate to the release directory, and run:
   ```powershell
   cd path\to\release
   .\pbi-local-mcp.DiscoverCli.exe
   ```
   
   **Note**: In PowerShell, you must use `.\` prefix to run executables from the current directory.
   
   Follow the prompts to:
   - Select the Power BI Desktop instance (identified by port)
   - Choose the database/model to connect to
   
   This creates a `.env` file with `PBI_PORT` and `PBI_DB_ID` in the release directory, which you can reference in your MCP configuration or ignore if you specify the port directly.

4. **Configure MCP server** in your editor. For VS Code with Roo, create/edit `.roo/mcp.json`:
   ```json
   {
     "mcpServers": {
       "MCPBI": {
         "type": "stdio",
         "command": "path\\to\\release\\mcpbi.exe",
         "cwd": "path\\to\\release",
         "args": ["--port", "YOUR_PBI_PORT"],
         "disabled": false,
         "alwaysAllow": [
           "ListTables",
           "GetTableDetails",
           "GetTableColumns",
           "GetTableRelationships",
           "ListMeasures",
           "GetMeasureDetails",
           "PreviewTableData",
           "RunQuery",
           "ValidateDaxSyntax",
           "AnalyzeQueryPerformance",
           "ListFunctions",
           "GetFunctionDetails"
         ]
       }
     }
   }
   ```
   Replace `path\\to\\release` with your actual release directory path and `YOUR_PBI_PORT` with the port number from PBI instance.

5. **Restart your editor** to load the MCP server configuration.

### Setup from Source (For Development)

1. **Clone the repository**:
   ```sh
   git clone <repository-url>
   cd MCPBI
   ```

2. **Build the project**:
   ```sh
   dotnet build
   ```

3. **Open Power BI Desktop** with a PBIX file.

4. **Run discovery** to create `.env` file:
   ```sh
   dotnet run --project pbi-local-mcp/pbi-local-mcp.csproj discover-pbi
   ```
   Follow prompts to select instance and database.

5. **Configure MCP server** in `.roo/mcp.json`:
   ```json
   {
     "mcpServers": {
       "mcpbi-dev": {
         "type": "stdio",
         "command": "dotnet",
         "cwd": "path\\to\\MCPBI",
         "envFile": "path\\to\\MCPBI\\.env",
         "args": [
           "exec",
           "path\\to\\MCPBI\\pbi-local-mcp\\bin\\Debug\\net8.0\\pbi-local-mcp.dll"
         ],
         "disabled": false,
         "alwaysAllow": [
           "ListTables",
           "GetTableDetails",
           "GetTableColumns",
           "GetTableRelationships",
           "ListMeasures",
           "GetMeasureDetails",
           "PreviewTableData",
           "RunQuery",
           "ValidateDaxSyntax",
           "AnalyzeQueryPerformance",
           "ListFunctions",
           "GetFunctionDetails"
         ]
       }
     }
   }
   ```
   Replace `path\\to\\MCPBI` with your actual repository path.

6. **Restart your editor** to load the MCP server.

### Configuration Notes
- **Use either port or envFile**: You can specify the Power BI port directly in `args` or use `envFile` to load from `.env`.
- **Port argument**: The `--port` argument in the release configuration connects to the specific Power BI Desktop instance on that port
- **envFile**: The development setup uses `envFile` to automatically load `PBI_PORT` and `PBI_DB_ID` from `.env`
- **alwaysAllow**: Lists all tools that can be used without requiring user approval for each invocation
- **Working directory**: The `cwd` parameter sets the working directory where the `.env` file is located