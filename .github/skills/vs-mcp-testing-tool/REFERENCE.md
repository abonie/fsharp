# MCP Tools Reference

Complete reference for all Visual Studio MCP server tools provided by `Microsoft.Copilot.Testing.MCP.VisualStudio`.

## Solution Management

### `vs-open-solution`

Opens a solution file and waits for full loading.

```
vs-open-solution path="C:\path\to\MySolution.sln"
```

- Accepts `.sln` and `.slnx` files.
- Blocks until the solution is fully loaded in Visual Studio.

### `vs-open-file`

Opens a file in the Visual Studio editor.

```
vs-open-file filePath="C:\path\to\MyFile.cs"
```

### `vs-close`

Closes Visual Studio.

```
vs-close
```

## Output Window

### `output-window-get-content`

Gets content from a specific output pane by name.

```
output-window-get-content paneName="GitHub Copilot Testing"
output-window-get-content paneName="Build"
```

Key panes:

| Pane | Content |
|---|---|
| `Build` | MSBuild compilation output |
| `Package Manager` | NuGet restore and package issues |

### `output-window-get-test-content`

Gets content from the Test output pane for detailed test results.

```
output-window-get-test-content
```

## Test Explorer

### `test-explorer-run-all-tests`

Executes all tests in the solution. This call is asynchronous — always follow with `test-explorer-wait-complete`.

```
test-explorer-run-all-tests
```

### `test-explorer-wait-complete`

Waits for the test run to finish.

```
test-explorer-wait-complete
```

### `test-explorer-verify-all-tests-passed`

Checks if all tests in the solution passed. Returns a quick pass/fail status.

```
test-explorer-verify-all-tests-passed
```

## Error List

### `vs-get-error-list`

Gets diagnostics from the Visual Studio Error List window.

```
vs-get-error-list
vs-get-error-list severity=error
```

- **`severity`** (optional): Filter results by severity. Accepted values: `error`, `warning`, `message`, `all` (default).
- Returns each diagnostic in the format `[severity] file(line,column): description`.
- Returns a summary header with match count and active filter.

## VS Editor

### `vs-inspect-lightbulb`

Inspects whether a light bulb is present at a file/line and returns available code action titles.

```
vs-inspect-lightbulb filePath="C:\path\to\File.cs" lineNumber=42
```

- Uses 1-based line numbers.
- Returns whether the light bulb is present and the list of offered actions.

## MCP Server Setup

### Prerequisites

1. Check if `vs-open-solution` tool is available. If it is, the server is already set up.
2. Run `./cibuild.cmd` to build the solution.
3. Set up the VS MCP server following `src/Microsoft.Copilot.Testing.MCP.VisualStudio/README.md`.
4. Configure your MCP client (e.g., `mcp.json`) to point to `bin\Microsoft.Copilot.Testing.MCP.VisualStudio\Release\net472\dotnet-codetesting-mcp-vs.exe`.
5. If testing against an experimental VS hive, pass `--hive Exp` in the MCP server args.

### Configuration Options

- **Hive Selection:** Use `--hive` parameter for experimental or specific VS installations.
- **Debugging:** Set `ATTACH_DEBUGGER=1` environment variable to debug the MCP server. Diagnostic output is written to stderr.

### System Requirements

- Visual Studio 18
- .NET 8 SDK or later
- Valid GitHub Copilot subscription

### VS Settings

- **F# LSP settings:** Tools → Options → F# LSP → "Get diagnostics from"
