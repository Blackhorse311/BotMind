# C# Patterns Reference

## Contents
- Naming Conventions
- Error Handling Pattern
- Thread Safety Patterns
- State Machine Pattern
- Reflection Interop Pattern
- Anti-Patterns

## Naming Conventions

This codebase follows strict naming:

```csharp
// Constants: SCREAMING_SNAKE_CASE
private const float SCAN_INTERVAL = 2.0f;
private const int HEAL_AMOUNT_PER_TICK = 5;

// Private fields: _camelCase
private readonly object _teamLock = new();
private volatile bool _isInitialized;

// Local variables: camelCase
var botOwner = BotOwner;
var lootTarget = FindNearestLoot();

// Methods/Properties: PascalCase
public bool TrySummonMedicBuddy() { }
public float SearchRadius { get; set; }
```

## Error Handling Pattern

Every BigBrain callback MUST be wrapped:

```csharp
// GOOD - Defensive wrapper
public override Action GetNextAction()
{
    try
    {
        if (_currentTarget == null)
            return new Action(typeof(IdleLogic), "Idle");
        return new Action(typeof(LootCorpseLogic), "LootCorpse");
    }
    catch (Exception ex)
    {
        BotMindPlugin.Log?.LogError($"GetNextAction error: {ex.Message}");
        return new Action(typeof(IdleLogic), "Idle"); // Safe fallback
    }
}

// BAD - Unhandled exceptions crash the game
public override Action GetNextAction()
{
    return new Action(typeof(LootCorpseLogic), "LootCorpse");
}
```

**Why:** BigBrain catches nothing. One exception kills the brain layer permanently.

## Thread Safety Patterns

### Volatile Singleton

```csharp
public class MedicBuddyController
{
    private static volatile MedicBuddyController? _instance;
    private static readonly object _instanceLock = new();

    public static MedicBuddyController Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new MedicBuddyController();
                }
            }
            return _instance;
        }
    }
}
```

### Lock for Shared State

```csharp
private readonly List<BotOwner> _teamMembers = new();
private readonly object _teamLock = new();

public void AddTeamMember(BotOwner bot)
{
    lock (_teamLock)
    {
        _teamMembers.Add(bot);
    }
}

public BotOwner[] GetTeamSnapshot()
{
    lock (_teamLock)
    {
        return _teamMembers.ToArray(); // Return copy, not reference
    }
}
```

## State Machine Pattern

Used in LootCorpseLogic and MedicBuddyController:

```csharp
private enum ELootState
{
    MovingToCorpse,
    Initial,
    LootWeapon,
    CheckBackpack,
    LootAllCalculations,
    Exit
}

private ELootState _currentState = ELootState.MovingToCorpse;

public override void Update(ActionData data)
{
    switch (_currentState)
    {
        case ELootState.MovingToCorpse:
            if (ReachedTarget()) _currentState = ELootState.Initial;
            break;
        case ELootState.Initial:
            StartLooting();
            _currentState = ELootState.LootWeapon;
            break;
        // ... other states
    }
}
```

## Reflection Interop Pattern

For soft dependencies like SAIN:

```csharp
public static class SAINInterop
{
    private static readonly Type? _externalType;
    private static readonly MethodInfo? _timeSinceSenseMethod;
    private static bool _initialized;

    static SAINInterop()
    {
        try
        {
            _externalType = Type.GetType("SAIN.Plugin.External, SAIN");
            _timeSinceSenseMethod = _externalType?.GetMethod(
                "TimeSinceSenseEnemy",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(BotOwner) },
                null);
            _initialized = _externalType != null;
        }
        catch
        {
            _initialized = false;
        }
    }

    public static float TimeSinceSenseEnemy(BotOwner bot)
    {
        if (!_initialized || _timeSinceSenseMethod == null)
            return float.MaxValue; // Fallback: assume no enemy

        return (float)(_timeSinceSenseMethod.Invoke(null, new object[] { bot }) ?? float.MaxValue);
    }
}
```

## Anti-Patterns

### WARNING: Catching Exception Without Logging

**The Problem:**

```csharp
// BAD - Silent failure
try { ProcessLoot(); }
catch { } // Swallowed
```

**Why This Breaks:** You'll never know why bots stopped looting. Debugging becomes impossible.

**The Fix:**

```csharp
// GOOD - Log and handle
try { ProcessLoot(); }
catch (Exception ex)
{
    BotMindPlugin.Log?.LogError($"ProcessLoot failed: {ex.Message}");
    _currentState = ELootState.Exit; // Recover to safe state
}
```

### WARNING: Mutable Static State

**The Problem:**

```csharp
// BAD - Race conditions
public static List<BotOwner> ActiveBots = new();
```

**Why This Breaks:** Multiple threads access this during bot spawning. Crashes or corrupted state.

**The Fix:**

```csharp
// GOOD - Thread-safe access
private static readonly List<BotOwner> _activeBots = new();
private static readonly object _botsLock = new();

public static void AddBot(BotOwner bot)
{
    lock (_botsLock) { _activeBots.Add(bot); }
}