# Unity Patterns Reference

## Contents
- Transform and GameObject Handling
- Physics Query Patterns
- NavMesh Navigation
- Performance Anti-Patterns
- Vector Math Patterns

---

## Transform and GameObject Handling

### Caching Transform References

```csharp
// GOOD - Cache in Awake/Start, reuse
private Transform _transform;
private void Awake() => _transform = transform;

public void Update()
{
    Vector3 pos = _transform.position; // Fast
}

// BAD - Accessing transform property repeatedly
public void Update()
{
    Vector3 pos = transform.position; // Property lookup each time
    Vector3 fwd = transform.forward;  // Another lookup
}
```

### Null-Safe GameObject Access

```csharp
// GOOD - Null propagation for Unity objects
var component = gameObject?.GetComponent<SomeComponent>();
if (component != null)
{
    // Use component
}

// WARNING: Unity's == operator override
// Unity objects can be "null" even when not actually null (destroyed objects)
// Always use explicit null check or bool conversion
if (gameObject) // Implicit bool - handles destroyed objects
{
    // Safe to use
}
```

---

## Physics Query Patterns

### Non-Allocating Overlap Queries

```csharp
// GOOD - Pre-allocated buffer, no GC pressure
private readonly Collider[] _overlapBuffer = new Collider[128];

public int FindNearbyTargets(Vector3 center, float radius, int layerMask)
{
    return Physics.OverlapSphereNonAlloc(center, radius, _overlapBuffer, layerMask);
}

// BAD - Allocates new array every call
public Collider[] FindNearbyTargets(Vector3 center, float radius)
{
    return Physics.OverlapSphere(center, radius); // GC allocation
}
```

### Layer Mask Best Practices

```csharp
// GOOD - Cache layer masks at startup
private static readonly int LOOT_LAYER_MASK = LayerMask.GetMask("Loot", "Interactive", "Terrain");

public void ScanArea()
{
    Physics.OverlapSphereNonAlloc(pos, radius, buffer, LOOT_LAYER_MASK);
}

// BAD - Computing mask every frame
public void ScanArea()
{
    int mask = LayerMask.GetMask("Loot", "Interactive"); // String lookup each time
}
```

### Raycast for Line of Sight

```csharp
public bool HasLineOfSight(Vector3 from, Vector3 to, int obstacleMask)
{
    Vector3 direction = to - from;
    float distance = direction.magnitude;
    
    // Use RaycastNonAlloc for frequent checks
    return !Physics.Raycast(from, direction.normalized, distance, obstacleMask);
}
```

---

## NavMesh Navigation

### Position Validation Pattern

```csharp
public bool IsPositionReachable(Vector3 start, Vector3 target)
{
    // First check if target is on NavMesh
    if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        return false;
    
    // Then verify path exists
    NavMeshPath path = new NavMeshPath();
    if (!NavMesh.CalculatePath(start, hit.position, NavMesh.AllAreas, path))
        return false;
    
    return path.status == NavMeshPathStatus.PathComplete;
}
```

### WARNING: NavMesh Off-Mesh Links

**The Problem:**
```csharp
// BAD - Assumes direct path
NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path);
// May fail if path requires jumping/climbing
```

**Why This Breaks:**
EFT maps have complex geometry. NavMesh links for ladders, jumps, and doors require special handling.

**The Fix:**
```csharp
// GOOD - Check path status and handle incomplete
if (path.status != NavMeshPathStatus.PathComplete)
{
    // Path requires off-mesh link or is blocked
    // Fallback to partial navigation or abort
}
```

---

## Performance Anti-Patterns

### WARNING: Find Methods in Update

**The Problem:**
```csharp
// BAD - O(n) search every frame
void Update()
{
    var player = GameObject.Find("Player");
    var bots = GameObject.FindObjectsOfType<BotOwner>();
}
```

**Why This Breaks:**
These methods traverse the entire scene hierarchy. In SPT with many bots, this causes severe frame drops.

**The Fix:**
```csharp
// GOOD - Cache references, use events
private Player _player;
private readonly List<BotOwner> _bots = new();

void Start()
{
    _player = FindObjectOfType<Player>();
    // Subscribe to spawn events instead of polling
    BotSpawner.OnBotSpawned += OnBotSpawned;
}
```

### WARNING: GetComponent in Hot Paths

**The Problem:**
```csharp
// BAD - Reflection-based lookup each call
void Update()
{
    var health = GetComponent<HealthController>();
    health.DoSomething();
}
```

**The Fix:**
```csharp
// GOOD - Cache in Awake
private HealthController _health;
void Awake() => _health = GetComponent<HealthController>();
```

---

## Vector Math Patterns

### Distance Comparisons

```csharp
// GOOD - Compare squared distances (avoids sqrt)
float maxDistSq = maxDistance * maxDistance;
if ((target - origin).sqrMagnitude < maxDistSq)
{
    // Within range
}

// BAD - Unnecessary sqrt operation
if (Vector3.Distance(target, origin) < maxDistance)
{
    // Same result, slower
}
```

### Direction and Facing

```csharp
// Check if target is in front of bot
public bool IsTargetInFront(Transform bot, Vector3 targetPos, float fovDegrees = 90f)
{
    Vector3 directionToTarget = (targetPos - bot.position).normalized;
    float dot = Vector3.Dot(bot.forward, directionToTarget);
    float threshold = Mathf.Cos(fovDegrees * 0.5f * Mathf.Deg2Rad);
    return dot >= threshold;
}
```

### Flat Distance (Ignoring Y)

```csharp
// For ground-based distance checks
public float GetFlatDistance(Vector3 a, Vector3 b)
{
    float dx = a.x - b.x;
    float dz = a.z - b.z;
    return Mathf.Sqrt(dx * dx + dz * dz);
}