# BepInEx Workflows Reference

## Contents
- Build and Deploy
- Debug Workflow
- Configuration Testing
- Release Checklist

## Build and Deploy

### Local Development Build

```bash
# Set SPT path (PowerShell)
$env:SPT_PATH = "H:\SPT"

# Build client plugin
dotnet build src/client/Blackhorse311.BotMind.csproj

# Output auto-copied to: BepInEx/plugins/Blackhorse311-BotMind/
```

### Project File Setup

```xml
<!-- Blackhorse311.BotMind.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <AssemblyName>Blackhorse311.BotMind</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="0Harmony">
      <HintPath>$(SPT_PATH)\BepInEx\core\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx">
      <HintPath>$(SPT_PATH)\BepInEx\core\BepInEx.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(SPT_PATH)\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- Auto-copy to SPT on build -->
  <Target Name="CopyToSPT" AfterTargets="Build" Condition="'$(SPT_PATH)' != ''">
    <Copy SourceFiles="$(TargetPath)"
          DestinationFolder="$(SPT_PATH)\BepInEx\plugins\Blackhorse311-BotMind\" />
  </Target>
</Project>
```

## Debug Workflow

### Enable BepInEx Console

Edit `BepInEx/config/BepInEx.cfg`:

```ini
[Logging.Console]
Enabled = true

[Logging.Disk]
Enabled = true
LogLevels = All
```

### Debug Build Workflow

Copy this checklist and track progress:
- [ ] Set `DebugMode = true` in mod config
- [ ] Build with Debug configuration
- [ ] Launch SPT client
- [ ] Open BepInEx console (` key)
- [ ] Filter log for your plugin: search "BotMind"
- [ ] Verify initialization messages appear
- [ ] Test target functionality
- [ ] Check for error messages

### Iterative Debug Loop

1. Make code changes
2. Build: `dotnet build src/client/Blackhorse311.BotMind.csproj`
3. Launch SPT and test
4. Check console for errors
5. If errors found, fix and repeat from step 1
6. Only proceed when functionality works

### Common Debug Log Points

```csharp
// Plugin load verification
Log.LogInfo($"{PLUGIN_NAME} is loading...");
Log.LogDebug("Step 1: Initializing configuration...");
Log.LogDebug("Step 2: Applying Harmony patches...");
Log.LogDebug("Step 3: Registering BigBrain layers...");
Log.LogInfo($"{PLUGIN_NAME} loaded successfully!");

// Runtime state tracking
Log.LogDebug($"[{BotOwner.name}] Layer activated: {GetName()}");
Log.LogDebug($"[{BotOwner.name}] Current state: {_currentState}");
```

## Configuration Testing

### Test Config Changes

1. Modify `BepInEx/config/com.blackhorse311.botmind.cfg`
2. Start new raid (config loaded on raid start)
3. Verify behavior matches config values
4. Check console for config parsing errors

### Config File Location

```
BepInEx/
└── config/
    └── com.blackhorse311.botmind.cfg
```

### Expected Config Format

```ini
[General]
Enable Looting = true
Enable Questing = true
Enable MedicBuddy = true

[Looting]
Search Radius = 50
Minimum Item Value = 5000
Loot Corpses = true
Loot Containers = true
Loot Loose Items = true

[MedicBuddy]
Summon Keybind = F10
Cooldown = 300
Team Size = 4
PMC Raids Only = true
```

## Release Checklist

Copy this checklist and track progress:
- [ ] Update version in `[BepInPlugin]` attribute
- [ ] Build Release configuration: `dotnet build -c Release`
- [ ] Run unit tests: `dotnet test`
- [ ] Test all major features in-game
- [ ] Verify no error logs during normal operation
- [ ] Check config generates with correct defaults
- [ ] Test with dependencies (BigBrain) present
- [ ] Test graceful degradation without optional dependencies (SAIN)
- [ ] Package DLL and any required assets
- [ ] Update changelog/release notes

### Release Build

```bash
# Clean build
dotnet clean
dotnet build src/client/Blackhorse311.BotMind.csproj -c Release

# Output: bin/Release/netstandard2.1/Blackhorse311.BotMind.dll
```

### Package Structure

```
Blackhorse311-BotMind/
├── BepInEx/
│   └── plugins/
│       └── Blackhorse311-BotMind/
│           └── Blackhorse311.BotMind.dll
└── user/
    └── mods/
        └── Blackhorse311-BotMind/
            └── (server mod files)