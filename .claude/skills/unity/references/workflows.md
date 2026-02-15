# Unity Workflows Reference

## Contents
- Bot Navigation Workflow
- Physics Scanning Workflow
- Coroutine Management
- Unity Object Lifecycle
- Debugging Unity in SPT

---

## Bot Navigation Workflow

### Moving Bot to Target Position

Copy this checklist and track progress:
- [ ] Step 1: Validate target is on NavMesh
- [ ] Step 2: Check path exists and is complete
- [ ] Step 3: Issue move command via BotOwner
- [ ] Step 4: Monitor arrival/stuck state

```csharp
public bool TryNavigateTo(BotOwner bot, Vector3 target)
{
    // Step 1: Validate NavMesh position
    if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, NavMesh.AllAreas))
    {
        BotMindPlugin.Log?.LogWarning($"Target {target} not on NavMesh");
        return false;
    }
    
    // Step 2: Verify path exists
    NavMeshPath path = new NavMeshPath();
    Vector3 botPos = bot.Transform.position;
    if (!NavMesh.CalculatePath(botPos, hit.position, NavMesh.AllAreas, path))
        return false;
    
    if (path.status != NavMeshPathStatus.PathComplete)
    {
        BotMindPlugin.Log?.LogDebug($"Path incomplete: {path.status}");
        return false;
    }
    
    // Step 3: Issue movement
    bot.GoToPoint(hit.position, true, -1f, false, true);
    return true;
}
```

### Stuck Detection Pattern

```csharp
private Vector3 _lastPosition;
private float _stuckTimer;
private const float STUCK_THRESHOLD = 3f;
private const float MIN_MOVEMENT = 0.5f;

public bool IsStuck(Vector3 currentPos)
{
    float moved = Vector3.Distance(currentPos, _lastPosition);
    
    if (moved < MIN_MOVEMENT)
    {
        _stuckTimer += Time.deltaTime;
        if (_stuckTimer > STUCK_THRESHOLD)
            return true;
    }
    else
    {
        _stuckTimer = 0f;
        _lastPosition = currentPos;
    }
    return false;
}
```

---

## Physics Scanning Workflow

### Periodic Area Scan

1. Configure scan interval (avoid every frame)
2. Use NonAlloc methods with pre-allocated buffers
3. Filter results by type/state
4. Cache valid targets

```csharp
private float _lastScanTime;
private const float SCAN_INTERVAL = 0.5f; // Scan twice per second
private readonly Collider[] _scanBuffer = new Collider[64];
private readonly List<LootableContainer> _containers = new();

public void UpdateScan(Vector3 position, float radius)
{
    if (Time.time - _lastScanTime < SCAN_INTERVAL)
        return;
    
    _lastScanTime = Time.time;
    _containers.Clear();
    
    int count = Physics.OverlapSphereNonAlloc(
        position, radius, _scanBuffer, 
        LayerMask.GetMask("Interactive"));
    
    for (int i = 0; i < count; i++)
    {
        if (_scanBuffer[i].TryGetComponent<LootableContainer>(out var container))
        {
            if (!container.IsSearched)
                _containers.Add(container);
        }
    }
}
```

---

## Coroutine Management

### Starting and Stopping Coroutines

```csharp
private Coroutine _healingCoroutine;

public void StartHealing()
{
    // Stop existing if running
    if (_healingCoroutine != null)
        StopCoroutine(_healingCoroutine);
    
    _healingCoroutine = StartCoroutine(HealingRoutine());
}

public void StopHealing()
{
    if (_healingCoroutine != null)
    {
        StopCoroutine(_healingCoroutine);
        _healingCoroutine = null;
    }
}

private IEnumerator HealingRoutine()
{
    while (_isHealing)
    {
        ApplyHealTick();
        yield return new WaitForSeconds(1f);
    }
    _healingCoroutine = null;
}
```

### WARNING: Coroutines on Destroyed Objects

**The Problem:**
```csharp
// BAD - Coroutine continues after object destroyed
StartCoroutine(LongRunningTask());
// Object gets destroyed mid-coroutine
// NullReferenceException on next yield
```

**The Fix:**
```csharp
private IEnumerator SafeCoroutine()
{
    while (someCondition)
    {
        if (this == null) yield break; // Early exit if destroyed
        
        DoWork();
        yield return new WaitForSeconds(1f);
    }
}

private void OnDestroy()
{
    StopAllCoroutines(); // Clean up on destroy
}
```

---

## Unity Object Lifecycle

### Initialization Order in BepInEx Context

```
1. BepInEx loads plugin DLL
2. Plugin constructor runs (avoid Unity calls here)
3. Awake() - Safe to cache components
4. OnEnable() - Subscribe to events
5. Start() - Safe to access other objects
6. Update()/LateUpdate() - Main loop
7. OnDisable() - Unsubscribe from events
8. OnDestroy() - Final cleanup
```

### Safe Initialization Pattern

```csharp
public class MedicBuddyController : MonoBehaviour
{
    private bool _initialized;
    
    private void Start()
    {
        try
        {
            Initialize();
            _initialized = true;
        }
        catch (Exception ex)
        {
            BotMindPlugin.Log?.LogError($"Init failed: {ex}");
            enabled = false; // Disable component on failure
        }
    }
    
    private void Update()
    {
        if (!_initialized) return;
        // Safe to run logic
    }
}
```

---

## Debugging Unity in SPT

### Runtime Inspection via BepInEx Console

```csharp
// Add debug commands via BepInEx config
[HarmonyPatch]
public class DebugPatch
{
    [HarmonyPatch(typeof(Player), "Update")]
    [HarmonyPostfix]
    public static void DebugOutput(Player __instance)
    {
        if (Input.GetKeyDown(KeyCode.F11))
        {
            var pos = __instance.Transform.position;
            BotMindPlugin.Log?.LogInfo($"Player pos: {pos}");
            
            // Check NavMesh
            if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas))
                BotMindPlugin.Log?.LogInfo($"NavMesh valid at: {hit.position}");
        }
    }
}
```

### Validation Feedback Loop

1. Make changes to Unity-interacting code
2. Build: `dotnet build src/client/Blackhorse311.BotMind.csproj`
3. Launch SPT, check BepInEx console for errors
4. If errors occur, check:
   - Null references (destroyed objects)
   - NavMesh validity
   - Layer mask correctness
5. Fix issues and repeat from step 2

### Common Unity Errors in SPT

| Error | Cause | Fix |
|-------|-------|-----|
| `MissingReferenceException` | Accessing destroyed GameObject | Add null checks, use `if (gameObject)` |
| `NavMeshPath incomplete` | Target off NavMesh or unreachable | Use `NavMesh.SamplePosition` first |
| `Physics query returns 0` | Wrong layer mask | Verify with `LayerMask.GetMask` |
| `Coroutine null reference` | Object destroyed mid-coroutine | Check `this == null` in coroutine |