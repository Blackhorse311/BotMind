# xUnit Test Patterns Reference

## Contents
- Naming Conventions
- FluentAssertions Patterns
- Mocking EFT/Unity Dependencies
- Testing BigBrain Layers
- Anti-Patterns

## Naming Conventions

Follow `MethodName_Scenario_ExpectedResult`:

```csharp
// GOOD - Clear what's being tested
public void FindLootableTargets_EmptyWorld_ReturnsEmptyList()
public void IsActive_BotInCombat_ReturnsFalse()
public void CalculatePriority_HighValueCloseItem_ReturnsHighScore()

// BAD - Vague or missing scenario
public void TestFindTargets()           // What scenario?
public void LootingWorks()              // What does "works" mean?
public void IsActive_ReturnsFalse()     // When? Why?
```

## FluentAssertions Patterns

### Collection Assertions

```csharp
// Checking contents
targets.Should().HaveCount(3);
targets.Should().Contain(x => x.Value > 5000);
targets.Should().BeInDescendingOrder(x => x.Priority);
targets.Should().OnlyContain(x => x.IsReachable);

// Empty/null checks
targets.Should().BeEmpty();
targets.Should().NotBeNullOrEmpty();
```

### Numeric Assertions

```csharp
// Exact and approximate
distance.Should().Be(10f);
distance.Should().BeApproximately(10f, precision: 0.01f);
distance.Should().BeInRange(5f, 15f);
health.Should().BePositive();
```

### Exception Assertions

```csharp
// Expecting exceptions
Action act = () => layer.Update(null);
act.Should().Throw<ArgumentNullException>()
   .WithParameterName("data");

// Not throwing
act.Should().NotThrow();
```

### Object Comparison

```csharp
// Deep equality
config.Should().BeEquivalentTo(expectedConfig);

// Specific properties
result.Should().BeEquivalentTo(expected, options => 
    options.Excluding(x => x.Timestamp));
```

## Mocking EFT/Unity Dependencies

### Mock BotOwner

```csharp
private Mock<BotOwner> CreateMockBotOwner(bool inCombat = false, float health = 100f)
{
    var mock = new Mock<BotOwner>();
    
    // Memory subsystem
    var memoryMock = new Mock<BotMemory>();
    memoryMock.Setup(m => m.IsUnderFire).Returns(inCombat);
    mock.Setup(b => b.Memory).Returns(memoryMock.Object);
    
    // Health
    mock.Setup(b => b.HealthStatus).Returns(health > 50 ? ETagStatus.Healthy : ETagStatus.Injured);
    
    return mock;
}
```

### Mock Vector3 Positions

```csharp
// Unity Vector3 is a struct, use directly
var botPosition = new Vector3(100f, 0f, 100f);
var targetPosition = new Vector3(110f, 0f, 100f);

mockBotOwner.Setup(b => b.Position).Returns(botPosition);

var distance = Vector3.Distance(botPosition, targetPosition);
distance.Should().BeApproximately(10f, 0.001f);
```

## Testing BigBrain Layers

### Layer Activation Tests

```csharp
[Fact]
public void IsActive_LootAvailableAndSafe_ReturnsTrue()
{
    // Arrange
    var mockBotOwner = CreateMockBotOwner(inCombat: false);
    var mockLootFinder = new Mock<ILootFinder>();
    mockLootFinder.Setup(f => f.HasLootInRange()).Returns(true);
    
    var layer = new LootingLayer(mockBotOwner.Object, mockLootFinder.Object);
    
    // Act & Assert
    layer.IsActive().Should().BeTrue();
}

[Fact]
public void IsActive_InCombat_ReturnsFalseEvenWithLoot()
{
    var mockBotOwner = CreateMockBotOwner(inCombat: true);
    var mockLootFinder = new Mock<ILootFinder>();
    mockLootFinder.Setup(f => f.HasLootInRange()).Returns(true);
    
    var layer = new LootingLayer(mockBotOwner.Object, mockLootFinder.Object);
    
    layer.IsActive().Should().BeFalse("combat takes priority over looting");
}
```

### Logic State Transitions

```csharp
[Theory]
[InlineData(ELootState.MovingToTarget, 0.5f, ELootState.Looting)]   // Close enough
[InlineData(ELootState.MovingToTarget, 10f, ELootState.MovingToTarget)] // Still moving
[InlineData(ELootState.Looting, 0f, ELootState.Complete)]           // Timer done
public void Update_TransitionsCorrectly(ELootState initial, float distance, ELootState expected)
{
    var logic = CreateLogicInState(initial, distanceToTarget: distance);
    
    logic.Update(new ActionData());
    
    logic.CurrentState.Should().Be(expected);
}
```

## Anti-Patterns

### WARNING: Testing Implementation Details

**The Problem:**

```csharp
// BAD - Testing private method calls
[Fact]
public void FindTargets_CallsPhysicsOverlapSphere()
{
    var finder = new LootFinder();
    finder.FindTargets();
    
    // Can't verify Physics.OverlapSphere was called - it's static Unity API
}
```

**Why This Breaks:**
Refactoring the implementation breaks tests even when behavior is correct.

**The Fix:**

```csharp
// GOOD - Test observable behavior
[Fact]
public void FindTargets_WithinRadius_ReturnsTargets()
{
    var finder = new LootFinder(radius: 50f);
    // Setup environment with known targets
    
    var results = finder.FindTargets(origin);
    
    results.Should().HaveCount(expectedCount);
}
```

### WARNING: Missing Arrange-Act-Assert Separation

**The Problem:**

```csharp
// BAD - Everything mixed together
[Fact]
public void TestLooting()
{
    var layer = new LootingLayer(new Mock<BotOwner>().Object);
    Assert.True(layer.IsActive() || !layer.IsActive()); // Always passes
    layer.Start();
    layer.Stop();
}
```

**The Fix:**

```csharp
// GOOD - Clear AAA structure
[Fact]
public void Start_InitializesState()
{
    // Arrange
    var layer = CreateLootingLayer();
    
    // Act
    layer.Start();
    
    // Assert
    layer.IsInitialized.Should().BeTrue();
}