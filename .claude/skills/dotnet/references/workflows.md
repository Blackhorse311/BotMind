# .NET Workflows Reference

## Contents
- Development Setup
- Build Workflows
- Deployment Workflow
- Troubleshooting

## Development Setup

### Initial Environment Setup

Copy this checklist and track progress:
- [ ] Install .NET 9 SDK
- [ ] Set SPT_PATH environment variable
- [ ] Verify game assemblies accessible
- [ ] Build client project
- [ ] Build server project
- [ ] Run tests

```powershell
# Set environment variable permanently (PowerShell Admin)
[Environment]::SetEnvironmentVariable("SPT_PATH", "H:\SPT", "User")

# Or for current session only
$env:SPT_PATH = "H:\SPT"

# Verify
Write-Host "SPT_PATH: $env:SPT_PATH"
Test-Path "$env:SPT_PATH\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll"
```

### Verify Dependencies

```bash
# Check .NET SDK version
dotnet --version

# List installed SDKs
dotnet --list-sdks

# Restore packages
dotnet restore src/client/Blackhorse311.BotMind.csproj
dotnet restore src/server/Blackhorse311.BotMind.Server.csproj
```

## Build Workflows

### Standard Development Build

```bash
# Clean previous build
dotnet clean src/client/Blackhorse311.BotMind.csproj

# Build debug (auto-copies to SPT if SPT_PATH set)
dotnet build src/client/Blackhorse311.BotMind.csproj

# Build server mod
dotnet build src/server/Blackhorse311.BotMind.Server.csproj
```

### Release Build

```bash
# Build optimized release
dotnet build src/client/Blackhorse311.BotMind.csproj -c Release

# Output location
# bin/Release/netstandard2.1/Blackhorse311.BotMind.dll
```

### Build with Verbose Output

**When:** Diagnosing reference resolution issues

```bash
dotnet build src/client/Blackhorse311.BotMind.csproj -v detailed 2>&1 | Out-File build.log
```

### Rebuild All

```bash
# Force full rebuild
dotnet build src/client/Blackhorse311.BotMind.csproj --no-incremental

# Or clean then build
dotnet clean && dotnet build
```

## Deployment Workflow

### Manual Deployment

```powershell
# Copy client plugin
$dest = "$env:SPT_PATH\BepInEx\plugins\Blackhorse311-BotMind"
New-Item -ItemType Directory -Force -Path $dest
Copy-Item "src\client\bin\Debug\netstandard2.1\Blackhorse311.BotMind.dll" $dest
Copy-Item "src\client\bin\Debug\netstandard2.1\Blackhorse311.BotMind.pdb" $dest

# Copy server mod
$serverDest = "$env:SPT_PATH\user\mods\Blackhorse311-BotMind"
New-Item -ItemType Directory -Force -Path $serverDest
Copy-Item "src\server\bin\Debug\net9.0\*" $serverDest -Recurse
```

### Package for Distribution

```bash
# Build release versions
dotnet build src/client/Blackhorse311.BotMind.csproj -c Release
dotnet build src/server/Blackhorse311.BotMind.Server.csproj -c Release

# Create distribution folder structure
# Blackhorse311-BotMind/
#   BepInEx/plugins/Blackhorse311-BotMind/
#     Blackhorse311.BotMind.dll
#   user/mods/Blackhorse311-BotMind/
#     Blackhorse311.BotMind.Server.dll
#     package.json
```

## Troubleshooting

### Missing Reference Assembly

**Symptom:** `error CS0246: The type or namespace 'BotOwner' could not be found`

**Diagnosis:**
```bash
# Check if SPT_PATH is set
echo $env:SPT_PATH

# Verify DLL exists
Test-Path "$env:SPT_PATH\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll"
```

**Fix:**
1. Set SPT_PATH environment variable
2. Restart terminal/IDE after setting
3. Run `dotnet restore`

### Build Succeeds but Plugin Not Loading

**Diagnosis checklist:**
- [ ] DLL copied to correct location (`BepInEx/plugins/`)
- [ ] Not in a subfolder unless configured
- [ ] BepInEx console shows plugin loading
- [ ] Check BepInEx/LogOutput.log for errors

### Assembly Version Conflicts

**Symptom:** `System.TypeLoadException` or `MissingMethodException` at runtime

**Fix:** Ensure `Private=false` on all game assembly references:

```xml
<Reference Include="Assembly-CSharp">
  <HintPath>$(ManagedPath)\Assembly-CSharp.dll</HintPath>
  <Private>false</Private>  <!-- Critical -->
</Reference>
```

### Incremental Build Not Detecting Changes

```bash
# Force full rebuild
dotnet build --no-incremental

# Or clean first
dotnet clean && dotnet build
```

### Test Project Cannot Find Client Assembly

Ensure test project references client project correctly:

```xml
<ItemGroup>
  <ProjectReference Include="..\client\Blackhorse311.BotMind.csproj" />
</ItemGroup>
```

Run tests with:
```bash
dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj --no-build
```

If tests fail to find types, rebuild:
```bash
dotnet build src/tests/Blackhorse311.BotMind.Tests.csproj
dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj