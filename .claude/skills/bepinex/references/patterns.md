# BepInEx Patterns Reference

## Contents
- Plugin Structure
- Configuration Patterns
- Logging Patterns
- Dependency Management
- Anti-Patterns

## Plugin Structure

### Standard Plugin Layout

```csharp
namespace Blackhorse311.BotMind
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
    public class BotMindPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.blackhorse311.botmind";
        public const string PLUGIN_NAME = "BotMind";
        public const string PLUGIN_VERSION = "1.0.0";
        
        public static BotMindPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        
        private void Awake()
        {
            Instance = this;
            Log = Logger;
            
            try
            {
                Log.LogInfo($"{PLUGIN_NAME} is loading...");
                BotMindConfig.Initialize(Config);
                new Harmony(PLUGIN_GUID).PatchAll();
                Log.LogInfo($"{PLUGIN_NAME} loaded successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
```

## Configuration Patterns

### Typed Config with Validation

```csharp
// GOOD - AcceptableValueRange enforces bounds
SearchRadius = config.Bind(
    "Looting",
    "Search Radius",
    50f,
    new ConfigDescription(
        "Loot search distance in meters",
        new AcceptableValueRange<float>(10f, 200f)
    )
);

// GOOD - AcceptableValueList for enums/options
SummonKey = config.Bind(
    "MedicBuddy",
    "Summon Keybind",
    KeyCode.F10,
    new ConfigDescription(
        "Key to summon medical team",
        new AcceptableValueList<KeyCode>(KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12)
    )
);
```

### WARNING: Accessing Config Before Initialization

**The Problem:**

```csharp
// BAD - Config accessed before Bind() called
public static ConfigEntry<bool> EnableLooting;

public void SomeMethod()
{
    if (EnableLooting.Value) // NullReferenceException!
}
```

**Why This Breaks:** ConfigEntry is null until `Bind()` is called in `Awake()`.

**The Fix:**

```csharp
// GOOD - Null check or initialize in static constructor
public static bool IsLootingEnabled => EnableLooting?.Value ?? true;
```

## Logging Patterns

### Log Levels

```csharp
Log.LogDebug("Detailed debug info");     // Development only
Log.LogInfo("Key events");               // Normal operation
Log.LogWarning("Recoverable issues");    // Something unexpected
Log.LogError("Failures with stack");     // Errors
Log.LogFatal("Unrecoverable crash");     // Plugin cannot function
```

### Structured Error Logging

```csharp
// GOOD - Context + exception details
public override bool IsActive()
{
    try
    {
        return CheckConditions();
    }
    catch (Exception ex)
    {
        BotMindPlugin.Log?.LogError(
            $"[{BotOwner?.name ?? "Unknown"}] IsActive error: {ex.Message}\n{ex.StackTrace}"
        );
        return false;
    }
}
```

### WARNING: Silent Failures

**The Problem:**

```csharp
// BAD - Swallows exceptions silently
try { DoSomething(); }
catch { }
```

**The Fix:**

```csharp
// GOOD - Log and fail safe
try { DoSomething(); }
catch (Exception ex)
{
    Log.LogError($"DoSomething failed: {ex.Message}");
    return defaultValue;
}
```

## Dependency Management

### Soft Dependency with Reflection

```csharp
public static class SAINInterop
{
    private static bool? _isAvailable;
    private static Type _externalType;
    private static MethodInfo _canBotQuestMethod;
    
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue) return _isAvailable.Value;
            
            try
            {
                _externalType = Type.GetType("SAIN.Plugin.External, SAIN");
                _isAvailable = _externalType != null;
                
                if (_isAvailable.Value)
                {
                    _canBotQuestMethod = _externalType.GetMethod("CanBotQuest");
                }
            }
            catch
            {
                _isAvailable = false;
            }
            
            return _isAvailable.Value;
        }
    }
    
    public static bool CanBotQuest(BotOwner bot, Vector3 position, float distance)
    {
        if (!IsAvailable) return true; // Fallback when SAIN not present
        
        try
        {
            return (bool)_canBotQuestMethod.Invoke(null, new object[] { bot, position, distance });
        }
        catch
        {
            return true;
        }
    }
}
```

## Anti-Patterns

### WARNING: Hardcoded Plugin GUIDs

**The Problem:**

```csharp
// BAD - GUID scattered throughout code
[BepInDependency("xyz.drakia.bigbrain")]
// ... later in another file ...
if (Chainloader.PluginInfos.ContainsKey("xyz.drakia.bigbrain"))
```

**The Fix:**

```csharp
// GOOD - Constants in one place
public static class PluginGUIDs
{
    public const string BIGBRAIN = "xyz.drakia.bigbrain";
    public const string SAIN = "me.sol.sain";
}

[BepInDependency(PluginGUIDs.BIGBRAIN)]
```

### WARNING: MonoBehaviour State in Static Fields

**The Problem:**

```csharp
// BAD - Static reference to MonoBehaviour survives scene reload
public static BotMindPlugin Instance;
private static List<BotOwner> _trackedBots = new();
```

**Why This Breaks:** Scene reloads destroy MonoBehaviours but static fields persist, causing null references.

**The Fix:**

```csharp
// GOOD - Clear state on scene change or use weak references
private void OnDestroy()
{
    _trackedBots.Clear();
    Instance = null;
}