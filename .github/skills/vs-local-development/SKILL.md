---
name: vs-local-development
description: Guide for local Visual Studio extension development, building, deploying, and testing the VS extension (VisualFSharp) in the experimental instance.
license: MIT
---

# Local Visual Studio Extension Development

This skill provides guidance on building, deploying, and iterating on the Visual Studio extension locally using the experimental instance of Visual Studio.

## Prerequisites

- Visual Studio 2026 installed with the **Visual Studio extension development** workload
- The `vs-mcp-testing-tools` skill must be loaded to understand how to use the MCP tools for testing after deployment

## Rebuild & Deploy Workflow

After making code changes to the VS extension, follow these steps **in order** to rebuild and deploy:

### 1. Close All Experimental VS Instances

Run the helper script to close any running experimental instances:

```powershell
.github\skills\vs-local-development\scripts\close-exp-instances.ps1
```

This kills all `devenv.exe` processes launched with `/rootsuffix`.

### 2. Rebuild the Solution

Run the build script:

```powershell
.\Build.cmd -c Debug
```

### 3. Deploy the VSIX

Choose a Visual Studio installation using `vswhere.exe`, which is installed with the Visual Studio Installer:

```powershell
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
& $vswhere -all -prerelease -format json -utf8 |
    ConvertFrom-Json |
    Select-Object instanceId, installationPath
```

Set `$instanceId` to the chosen installation's ID, then deploy:

```powershell
$instanceId = 'INSTANCE_ID'
.github\skills\vs-local-development\scripts\deploy-to-vs.ps1 `
    "artifacts\VSSetup\Debug\VisualFSharpDebug.vsix" -InstanceId $instanceId
```

The script installs only into that installation's experimental hive (`/rootSuffix:exp`), waits for the installer, then uses that same installation's `devenv.exe` to clear caches and update configuration. The executables do not need to be on `PATH`; an unknown instance ID or missing executable is an error.

### 4. Reopen VS

Open the selected installation's experimental instance:

```powershell
$installation = & $vswhere -all -prerelease -format json -utf8 |
    ConvertFrom-Json |
    Where-Object { $_.instanceId -eq $instanceId }
& (Join-Path $installation.installationPath 'Common7\IDE\devenv.exe') /rootsuffix exp
```

## Quick Reference (Copy-Paste)

Full sequence for a rebuild cycle:

```powershell
# 1. Stop experimental instances
.github\skills\vs-local-development\scripts\close-exp-instances.ps1

# 2. Rebuild
.\Build.cmd -c Debug

# 3. Select an instance ID reported by vswhere.exe and deploy
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$instanceId = 'INSTANCE_ID'
.github\skills\vs-local-development\scripts\deploy-to-vs.ps1 `
    "artifacts\VSSetup\Debug\VisualFSharpDebug.vsix" -InstanceId $instanceId

# 4. Reopen VS with the updated extension
$installation = & $vswhere -all -prerelease -format json -utf8 |
    ConvertFrom-Json |
    Where-Object { $_.instanceId -eq $instanceId }
& (Join-Path $installation.installationPath 'Common7\IDE\devenv.exe') /rootsuffix exp
```

## Key Concepts

### Experimental Instance

Visual Studio supports **experimental instances** (`/rootsuffix exp`) — isolated environments with separate extension registrations and settings. This allows developing and testing extensions without affecting your main VS installation.

## Troubleshooting

| Problem | Solution |
|---|---|
| VSIX install fails | Ensure all experimental VS instances are closed (`.github\skills\vs-local-development\scripts\close-exp-instances.ps1`) |
| Extension not appearing in VS | Re-run `deploy-to-vs.ps1` with the intended `-InstanceId` |
| Old version still loaded | Delete `artifacts/` and rebuild |
| Instance ID or executable not found | Check `vswhere.exe` output and ensure the selected Visual Studio installation is available |

## When to Use This Skill

Use this skill when:

- Making changes to the VS extension (`VisualFSharpFull` or `VisualFSharpDebug`)
- Needing to test extension changes locally in Visual Studio
- Troubleshooting VSIX deployment to the experimental instance
- Setting up a local VS extension development loop

## Related

- [vs-mcp-testing-tools](../vs-mcp-testing-tools/SKILL.md) — for automating VS via MCP tools after the extension is deployed
