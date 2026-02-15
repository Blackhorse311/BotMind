# .NET Patterns Reference

## Contents
- Project Configuration
- Reference Management
- Build Configuration
- Package Management
- Anti-Patterns

## Project Configuration

### Target Framework Selection

```xml
<!-- Client plugin - BepInEx requires netstandard2.1 -->
<PropertyGroup>
  <TargetFramework>netstandard2.1</TargetFramework>
  <LangVersion>12.0</LangVersion>
</PropertyGroup>

<!-- Server mod - SPT server uses .NET 9 -->
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

### Output Configuration

```xml
<PropertyGroup>
  <OutputType>Library</OutputType>
  <AssemblyName>Blackhorse311.BotMind</AssemblyName>
  <RootNamespace>Blackhorse311.BotMind</RootNamespace>
  <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
</PropertyGroup>
```

## Reference Management

### Game Assembly References

```xml
<!-- Define paths conditionally -->
<PropertyGroup>
  <SPTPath Condition="'$(SPT_PATH)' != ''">$(SPT_PATH)</SPTPath>
  <ManagedPath>$(SPTPath)\EscapeFromTarkov_Data\Managed</ManagedPath>
  <BepInExPath>$(SPTPath)\BepInEx</BepInExPath>
</PropertyGroup>

<!-- Reference game DLLs - Private=false prevents copying -->
<ItemGroup Condition="'$(SPTPath)' != ''">
  <Reference Include="Assembly-CSharp">
    <HintPath>$(ManagedPath)\Assembly-CSharp.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Reference Include="Comfort">
    <HintPath>$(ManagedPath)\Comfort.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

### BepInEx References

```xml
<ItemGroup Condition="'$(BepInExPath)' != ''">
  <Reference Include="BepInEx.Core">
    <HintPath>$(BepInExPath)\core\BepInEx.dll</HintPath>
    <Private>false</Private>
  </Reference>
  <Reference Include="0Harmony">
    <HintPath>$(BepInExPath)\core\0Harmony.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

## Build Configuration

### Debug vs Release

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DefineConstants>DEBUG;TRACE</DefineConstants>
  <DebugType>full</DebugType>
  <Optimize>false</Optimize>
</PropertyGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <DefineConstants>RELEASE</DefineConstants>
  <DebugType>pdbonly</DebugType>
  <Optimize>true</Optimize>
</PropertyGroup>
```

### Post-Build Events

```xml
<Target Name="CopyToSPT" AfterTargets="Build" Condition="'$(SPT_PATH)' != ''">
  <MakeDir Directories="$(SPT_PATH)\BepInEx\plugins\Blackhorse311-BotMind" />
  <Copy SourceFiles="$(TargetPath);$(TargetDir)$(TargetName).pdb" 
        DestinationFolder="$(SPT_PATH)\BepInEx\plugins\Blackhorse311-BotMind\" 
        SkipUnchangedFiles="true" />
  <Message Text="Copied to $(SPT_PATH)\BepInEx\plugins\Blackhorse311-BotMind\" Importance="high" />
</Target>
```

## Package Management

### Server Mod NuGet References

```xml
<ItemGroup>
  <PackageReference Include="SPTarkov.Common" Version="4.0.11" />
  <PackageReference Include="SPTarkov.DI" Version="4.0.11" />
  <PackageReference Include="SPTarkov.Server.Core" Version="4.0.11" />
</ItemGroup>
```

### Test Project References

```xml
<ItemGroup>
  <PackageReference Include="xunit" Version="2.9.*" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.*" />
  <PackageReference Include="FluentAssertions" Version="6.*" />
  <PackageReference Include="Moq" Version="4.*" />
  <PackageReference Include="coverlet.collector" Version="6.*" />
</ItemGroup>
```

## Anti-Patterns

### WARNING: Private=true for Game Assemblies

**The Problem:**

```xml
<!-- BAD - copies 50MB+ of DLLs to output -->
<Reference Include="Assembly-CSharp">
  <HintPath>$(ManagedPath)\Assembly-CSharp.dll</HintPath>
</Reference>
```

**Why This Breaks:**
1. Bloats plugin size from 100KB to 50MB+
2. Can cause assembly conflicts at runtime
3. Build takes longer copying unnecessary files

**The Fix:**

```xml
<!-- GOOD - reference only, don't copy -->
<Reference Include="Assembly-CSharp">
  <HintPath>$(ManagedPath)\Assembly-CSharp.dll</HintPath>
  <Private>false</Private>
</Reference>
```

### WARNING: Hardcoded SPT Path

**The Problem:**

```xml
<!-- BAD - works only on your machine -->
<Reference Include="Assembly-CSharp">
  <HintPath>H:\SPT\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll</HintPath>
</Reference>
```

**Why This Breaks:**
1. Fails on any other developer's machine
2. Fails in CI/CD environments
3. Path changes require editing csproj

**The Fix:**

```xml
<!-- GOOD - environment variable with fallback -->
<PropertyGroup>
  <SPTPath Condition="'$(SPT_PATH)' != ''">$(SPT_PATH)</SPTPath>
</PropertyGroup>
<Reference Include="Assembly-CSharp" Condition="'$(SPTPath)' != ''">
  <HintPath>$(SPTPath)\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll</HintPath>
  <Private>false</Private>
</Reference>