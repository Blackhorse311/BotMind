# C# Workflows Reference

## Contents
- Adding a New BigBrain Layer
- Implementing CustomLogic State Machine
- Adding Configuration Options
- Reflection-Based API Integration
- Debugging Runtime Issues

## Adding a New BigBrain Layer

### Workflow Checklist

Copy this checklist and track progress:
- [ ] Step 1: Create layer class extending `CustomLayer`
- [ ] Step 2: Implement required abstract methods
- [ ] Step 3: Register layer in `BotMindPlugin.cs`
- [ ] Step 4: Create associated logic classes
- [ ] Step 5: Test layer activation conditions

### Implementation

```csharp
// Step 1-2: Create layer in Modules/[Feature]/
public class PatrolLayer : CustomLayer
{
    public PatrolLayer(BotOwner bot, int priority) : base(bot, priority) { }

    public override string GetName() => "PatrolLayer";

    public override bool IsActive()
    {
        try
        {
            if (BotOwner?.IsDead == true) return false;
            if (!BotMindConfig.EnablePatrol.Value) return false;
            return SAINInterop.TimeSinceSenseEnemy(BotOwner) > 30f;
        }
        catch (Exception ex)
        {
            BotMindPlugin.Log?.LogError($"PatrolLayer.IsActive: {ex.Message}");
            return false;
        }
    }

    public override Action GetNextAction()
    {
        return new Action(typeof(PatrolLogic), "Patrol");
    }

    public override bool IsCurrentActionEnding() => false;
}
```

```csharp
// Step 3: Register in BotMindPlugin.cs
private void RegisterLayers()
{
    var brainNames = new List<string> { "PMC", "Assault", "BossKnight" };
    BrainManager.AddCustomLayer(typeof(PatrolLayer), brainNames, 20);
}
```

## Implementing CustomLogic State Machine

### Pattern

```csharp
public class LootContainerLogic : CustomLogic
{
    private enum EState { Moving, Opening, Looting, Done }
    private EState _state = EState.Moving;
    private LootableContainer? _target;

    public override void Start()
    {
        _target = FindNearestContainer();
        _state = EState.Moving;
    }

    public override void Update(ActionData data)
    {
        try
        {
            switch (_state)
            {
                case EState.Moving:
                    if (!NavigateToTarget()) return;
                    _state = EState.Opening;
                    break;

                case EState.Opening:
                    OpenContainer();
                    _state = EState.Looting;
                    break;

                case EState.Looting:
                    if (LootItems())
                        _state = EState.Done;
                    break;

                case EState.Done:
                    data.StopCurrentAction = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            BotMindPlugin.Log?.LogError($"LootContainerLogic: {ex.Message}");
            data.StopCurrentAction = true;
        }
    }

    public override void Stop()
    {
        _target = null;
        _state = EState.Moving;
    }
}
```

### Validation Loop

1. Implement state transitions
2. Test each state: `BotMindPlugin.Log?.LogDebug($"State: {_state}");`
3. If bot gets stuck, add timeout logic and repeat step 2
4. Only proceed when all states transition correctly

## Adding Configuration Options

See the **bepinex** skill for full configuration patterns.

```csharp
// In BotMindConfig.cs
public static ConfigEntry<float> PatrolRadius { get; private set; } = null!;

public static void Bind(ConfigFile config)
{
    PatrolRadius = config.Bind(
        "Patrol",
        "Patrol Radius",
        100f,
        new ConfigDescription(
            "Maximum patrol distance in meters",
            new AcceptableValueRange<float>(10f, 500f)));
}
```

## Reflection-Based API Integration

### Workflow Checklist

Copy this checklist and track progress:
- [ ] Step 1: Identify target assembly and type
- [ ] Step 2: Cache Type and MethodInfo in static constructor
- [ ] Step 3: Implement wrapper with null checks
- [ ] Step 4: Provide sensible fallback values
- [ ] Step 5: Add IsAvailable property for feature detection

### Implementation

```csharp
public static class ExternalModInterop
{
    private static readonly MethodInfo? _targetMethod;
    public static bool IsAvailable { get; }

    static ExternalModInterop()
    {
        try
        {
            var type = Type.GetType("ModName.ClassName, ModName");
            _targetMethod = type?.GetMethod("MethodName");
            IsAvailable = _targetMethod != null;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public static bool TryCallMethod(object arg, out object? result)
    {
        result = null;
        if (!IsAvailable) return false;

        try
        {
            result = _targetMethod!.Invoke(null, new[] { arg });
            return true;
        }
        catch (Exception ex)
        {
            BotMindPlugin.Log?.LogWarning($"Interop call failed: {ex.Message}");
            return false;
        }
    }
}
```

## Debugging Runtime Issues

### Console Logging Levels

```csharp
// Use appropriate levels
BotMindPlugin.Log?.LogDebug("Detailed state info");  // Development only
BotMindPlugin.Log?.LogInfo("Layer activated");       // Normal operation
BotMindPlugin.Log?.LogWarning("Fallback used");      // Degraded but working
BotMindPlugin.Log?.LogError("Critical failure");     // Needs attention
```

### Common Issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| Layer never activates | `IsActive()` throwing | Wrap in try-catch, log exception |
| Bot freezes mid-action | State machine stuck | Add timeout, force state transition |
| NullReferenceException | Game object destroyed | Check `BotOwner?.IsDead` first |
| Inconsistent behavior | Race condition | Add locks around shared state |

### Debug BuildDebugText

```csharp
public override void BuildDebugText(StringBuilder sb)
{
    sb.AppendLine($"State: {_currentState}");
    sb.AppendLine($"Target: {_target?.name ?? "None"}");
    sb.AppendLine($"Distance: {_distanceToTarget:F1}m");
}