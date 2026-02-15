# Harmony Workflows Reference

## Contents
- Creating a New Patch
- Debugging Patches
- Testing Patches
- Patch Priority and Ordering

## Creating a New Patch

### Workflow Checklist

Copy this checklist and track progress:
- [ ] Step 1: Identify target method signature with dnSpy/ILSpy
- [ ] Step 2: Create patch class in `src/client/Patches/`
- [ ] Step 3: Add `[HarmonyPatch]` attribute with correct target
- [ ] Step 4: Implement prefix/postfix with error handling
- [ ] Step 5: Build and verify patch applies in BepInEx console
- [ ] Step 6: Test in-game behavior

### Step 1: Find Target Method

Use dnSpy or ILSpy to decompile `Assembly-CSharp.dll`:

```bash
# Location of game assemblies
$env:SPT_PATH\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll
```

Note the exact:
- Namespace and class name
- Method name (check for overloads)
- Parameter types
- Return type

### Step 2: Create Patch Class

```csharp
// src/client/Patches/GameWorldPatches.cs
using System;
using HarmonyLib;
using EFT;
using Blackhorse311.BotMind.Modules.MedicBuddy;

namespace Blackhorse311.BotMind.Patches
{
    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    internal static class GameStartedPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                MedicBuddyController.Instance?.OnRaidStarted();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError(
                    $"GameStartedPatch error: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
```

### Step 3: Register Patches

Patches are auto-registered in `BotMindPlugin.cs`:

```csharp
public override void Load()
{
    Log = base.Log;
    
    var harmony = new Harmony("com.blackhorse311.botmind");
    harmony.PatchAll(); // Applies all [HarmonyPatch] in assembly
    
    Log.LogInfo($"BotMind patches applied: {harmony.GetPatchedMethods().Count()}");
}
```

## Debugging Patches

### Verify Patch Applied

Check BepInEx console on startup:

```
[Info   :   BepInEx] Loading [BotMind 1.0.0]
[Debug  :  Harmony] Patching GameWorld.OnGameStarted
[Debug  :  Harmony] Patching GameWorld.Dispose
[Info   :  BotMind] BotMind patches applied: 2
```

### List All Patches on a Method

```csharp
// Debug helper - call from plugin Load()
private void DebugPatches()
{
    var patches = Harmony.GetPatchInfo(
        typeof(GameWorld).GetMethod("OnGameStarted"));
    
    if (patches != null)
    {
        Log.LogInfo($"Prefixes: {patches.Prefixes.Count}");
        Log.LogInfo($"Postfixes: {patches.Postfixes.Count}");
        
        foreach (var prefix in patches.Prefixes)
        {
            Log.LogInfo($"  - {prefix.owner}: {prefix.PatchMethod.Name}");
        }
    }
}
```

### Common Issues

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| Patch not in list | Wrong method signature | Verify with decompiler |
| NullReferenceException | `__instance` is null | Add null check |
| Original never runs | Prefix returns false | Check return logic |
| Runs multiple times | Multiple harmony instances | Use unique harmony ID |

## Testing Patches

### Unit Testing with Harmony

See the **xunit** skill for test project setup.

```csharp
[Fact]
public void GameStartedPatch_CallsOnRaidStarted()
{
    // Arrange
    var mockController = new Mock<IMedicBuddyController>();
    MedicBuddyController.SetInstance(mockController.Object);
    
    // Act - Simulate patch execution
    var patchMethod = typeof(GameStartedPatch)
        .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
    patchMethod.Invoke(null, null);
    
    // Assert
    mockController.Verify(x => x.OnRaidStarted(), Times.Once);
}
```

### Integration Testing Checklist

Copy this checklist and track progress:
- [ ] Build plugin: `dotnet build src/client/Blackhorse311.BotMind.csproj`
- [ ] Copy to SPT: Auto-copied if `SPT_PATH` set
- [ ] Launch SPT and check BepInEx console for errors
- [ ] Enter raid and trigger patched behavior
- [ ] Verify expected behavior in-game
- [ ] Check logs for any error messages

## Patch Priority and Ordering

### Controlling Execution Order

```csharp
[HarmonyPatch(typeof(BotOwner), "Think")]
[HarmonyPriority(Priority.High)] // Run before other mods
internal static class ThinkPatch
{
    [HarmonyPrefix]
    private static void Prefix() { }
}
```

### Priority Values

| Priority | Value | Use Case |
|----------|-------|----------|
| First | 0 | Must run before everything |
| VeryHigh | 100 | Critical patches |
| High | 200 | Important, but not critical |
| Normal | 400 | Default |
| Low | 600 | Can run later |
| VeryLow | 800 | Should run near end |
| Last | 1000 | Must run after everything |

### SAIN Compatibility

When patching methods SAIN also patches, use lower priority:

```csharp
// Let SAIN's combat patches run first
[HarmonyPatch(typeof(BotOwner), "CalcGoal")]
[HarmonyPriority(Priority.Low)]
[HarmonyAfter("me.sol.sain")] // Explicit ordering
internal static class CalcGoalPatch
{
    [HarmonyPostfix]
    private static void Postfix(BotOwner __instance)
    {
        // Only modify if SAIN didn't handle it
        if (!SAINInterop.IsBotInCombat(__instance))
        {
            // Apply looting/questing logic
        }
    }
}