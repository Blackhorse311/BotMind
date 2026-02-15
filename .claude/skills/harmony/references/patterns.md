# Harmony Patterns Reference

## Contents
- Patch Types
- Parameter Injection
- Error Handling in Patches
- Anti-Patterns

## Patch Types

### Prefix - Intercept Before Execution

```csharp
[HarmonyPatch(typeof(BotSpawnerClass), nameof(BotSpawnerClass.SpawnBot))]
internal static class SpawnBotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(BotCreationDataClass data, ref BotOwner __result)
    {
        // Intercept MedicBuddy spawns to apply custom layers
        if (data.SpawnReason == "MedicBuddy")
        {
            __result = MedicBuddyController.SpawnCustomBot(data);
            return false; // Skip original SpawnBot
        }
        return true;
    }
}
```

### Postfix - React After Execution

```csharp
[HarmonyPatch(typeof(Player), nameof(Player.OnDead))]
internal static class PlayerDeathPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance, EDamageType damageType)
    {
        if (__instance.IsYourPlayer)
        {
            MedicBuddyController.Instance?.OnPlayerDied();
        }
    }
}
```

### Transpiler - IL Modification

**When:** Need surgical changes to method logic without full replacement.

```csharp
[HarmonyPatch(typeof(BotOwner), "CalcGoal")]
internal static class CalcGoalTranspiler
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        
        for (int i = 0; i < codes.Count; i++)
        {
            // Find and modify specific instruction
            if (codes[i].opcode == OpCodes.Ldc_R4 && 
                (float)codes[i].operand == 10f)
            {
                codes[i].operand = 50f; // Increase search radius
            }
        }
        
        return codes;
    }
}
```

## Parameter Injection

### Available Injected Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `__instance` | Declaring type | The object being patched |
| `__result` | Return type | Method return value (postfix/ref in prefix) |
| `__state` | Any | Pass data from prefix to postfix |
| `___fieldName` | Field type | Private field access (3 underscores) |
| `__originalMethod` | MethodBase | The patched method info |

### State Passing Between Prefix and Postfix

```csharp
[HarmonyPatch(typeof(BotOwner), "Think")]
internal static class ThinkTimingPatch
{
    [HarmonyPrefix]
    private static void Prefix(out Stopwatch __state)
    {
        __state = Stopwatch.StartNew();
    }
    
    [HarmonyPostfix]
    private static void Postfix(BotOwner __instance, Stopwatch __state)
    {
        __state.Stop();
        if (__state.ElapsedMilliseconds > 16)
        {
            BotMindPlugin.Log?.LogWarning(
                $"[{__instance.name}] Think took {__state.ElapsedMilliseconds}ms");
        }
    }
}
```

### Accessing Private Fields

```csharp
[HarmonyPostfix]
private static void Postfix(
    BotOwner __instance,
    bool ___isInCombat,           // Private field
    ref float ___lastScanTime)    // Writable private field
{
    if (!___isInCombat)
    {
        ___lastScanTime = Time.time; // Reset scan timer
    }
}
```

## Error Handling in Patches

### WARNING: Unhandled Exceptions in Patches

**The Problem:**

```csharp
// BAD - Exception crashes the entire method chain
[HarmonyPrefix]
private static void Prefix(BotOwner __instance)
{
    var items = __instance.GetLootItems(); // Might throw
    ProcessItems(items);
}
```

**Why This Breaks:**
1. Unhandled exception prevents original method from running
2. May corrupt game state if prefix partially executed
3. Other mods' patches on same method also fail

**The Fix:**

```csharp
// GOOD - Wrapped with fail-safe
[HarmonyPrefix]
private static bool Prefix(BotOwner __instance)
{
    try
    {
        var items = __instance.GetLootItems();
        ProcessItems(items);
        return true;
    }
    catch (Exception ex)
    {
        BotMindPlugin.Log?.LogError($"Prefix error: {ex.Message}");
        return true; // Let original run despite our failure
    }
}
```

## Anti-Patterns

### WARNING: Patching Hot Methods

**The Problem:**

```csharp
// BAD - Update runs 60+ times per second per bot
[HarmonyPatch(typeof(BotOwner), "Update")]
internal static class UpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(BotOwner __instance)
    {
        LootFinder.ScanForLoot(__instance); // Expensive!
    }
}
```

**Why This Breaks:**
1. 20 bots × 60 fps = 1200 calls/second
2. Causes severe frame drops
3. GC pressure from allocations

**The Fix:**

```csharp
// GOOD - Throttle with timer
[HarmonyPostfix]
private static void Postfix(BotOwner __instance)
{
    if (Time.time - _lastScanTime[__instance] < SCAN_INTERVAL)
        return;
    
    _lastScanTime[__instance] = Time.time;
    LootFinder.ScanForLoot(__instance);
}
```

### WARNING: Patching Generic Methods

**The Problem:**

```csharp
// BAD - Generic method without specifying type arguments
[HarmonyPatch(typeof(Container<>), "GetItem")]
```

**Why This Breaks:**
1. Harmony can't resolve open generic types
2. Patch silently fails to apply
3. No compile-time error

**The Fix:**

```csharp
// GOOD - Patch specific closed generic or use TargetMethod
[HarmonyPatch]
internal static class ContainerPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        return typeof(Container<Item>).GetMethod("GetItem");
    }
}