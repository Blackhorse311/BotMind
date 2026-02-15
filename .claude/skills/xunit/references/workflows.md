# xUnit Workflows Reference

## Contents
- Test-Driven Development Workflow
- Running Tests
- Adding New Test Classes
- CI Integration
- Debugging Failed Tests

## Test-Driven Development Workflow

### Red-Green-Refactor Cycle

Copy this checklist and track progress:
- [ ] Write failing test that defines expected behavior
- [ ] Run test to confirm it fails (Red)
- [ ] Write minimal code to pass test
- [ ] Run test to confirm it passes (Green)
- [ ] Refactor while keeping tests green
- [ ] Commit

### Example: Adding Value Filter to LootFinder

```csharp
// Step 1: Write failing test
[Fact]
public void FindTargets_MinValueSet_FiltersLowValueItems()
{
    var finder = new LootFinder(minValue: 5000);
    // Mock items: one worth 1000, one worth 10000
    
    var results = finder.FindTargets(origin);
    
    results.Should().OnlyContain(x => x.Value >= 5000);
}

// Step 2: Run test - FAILS (filter not implemented)
// Step 3: Implement filter in LootFinder.cs
// Step 4: Run test - PASSES
// Step 5: Refactor if needed
```

## Running Tests

### Command Reference

```bash
# All tests
dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj

# Specific test file
dotnet test --filter "FullyQualifiedName~LootFinderTests"

# Specific test method
dotnet test --filter "FindTargets_MinValueSet_FiltersLowValueItems"

# By trait/category
dotnet test --filter "Category=Looting"

# With detailed output
dotnet test --logger "console;verbosity=detailed"

# Stop on first failure
dotnet test -- xUnit.MaxParallelThreads=1 --fail-fast
```

### Validate Loop

1. Make code changes
2. Run: `dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj`
3. If tests fail, fix issues and repeat step 2
4. Only commit when all tests pass

## Adding New Test Classes

### File Location

```
src/tests/
├── Modules/
│   ├── Looting/
│   │   ├── LootFinderTests.cs
│   │   ├── LootingLayerTests.cs
│   │   └── LootCorpseLogicTests.cs
│   ├── Questing/
│   │   └── QuestManagerTests.cs
│   └── MedicBuddy/
│       └── MedicBuddyControllerTests.cs
├── Configuration/
│   └── BotMindConfigTests.cs
└── Interop/
    └── SAINInteropTests.cs
```

### Test Class Template

```csharp
using FluentAssertions;
using Moq;
using Xunit;
using Blackhorse311.BotMind.Modules.Looting;

namespace Blackhorse311.BotMind.Tests.Modules.Looting;

public class LootFinderTests
{
    private readonly Mock<BotOwner> _mockBotOwner;
    
    public LootFinderTests()
    {
        _mockBotOwner = CreateDefaultMockBotOwner();
    }
    
    private Mock<BotOwner> CreateDefaultMockBotOwner()
    {
        var mock = new Mock<BotOwner>();
        // Default setup
        return mock;
    }
    
    [Fact]
    public void MethodName_Scenario_ExpectedResult()
    {
        // Arrange
        
        // Act
        
        // Assert
    }
}
```

## CI Integration

### Build Verification

Tests run automatically on build when using `dotnet test`:

```bash
# CI script pattern
dotnet restore
dotnet build --no-restore
dotnet test --no-build --verbosity normal
```

### Coverage Reports

```bash
# Generate coverage
dotnet test --collect:"XPlat Code Coverage"

# Output in TestResults/*/coverage.cobertura.xml
```

## Debugging Failed Tests

### Common Failure Patterns

**Mock Not Setup:**
```
System.NullReferenceException: Object reference not set
```
Fix: Add missing `Setup()` call on mock

**Assertion Message:**
```csharp
// Add context to assertions
result.Should().BeTrue("because bot has no threats nearby");
```

**Async Test Issues:**
```csharp
// WRONG - doesn't await
[Fact]
public void AsyncMethod_Test()
{
    var result = service.DoAsync(); // Missing await
}

// RIGHT
[Fact]
public async Task AsyncMethod_ReturnsExpected()
{
    var result = await service.DoAsync();
    result.Should().NotBeNull();
}
```

### Isolating Failures

```bash
# Run single failing test with full output
dotnet test --filter "MethodName_Scenario_ExpectedResult" --logger "console;verbosity=detailed"
```

### Test Categories

Use traits to organize and run subsets:

```csharp
[Fact]
[Trait("Category", "Looting")]
[Trait("Speed", "Fast")]
public void QuickLootingTest() { }

// Run only fast looting tests
// dotnet test --filter "Category=Looting&Speed=Fast"