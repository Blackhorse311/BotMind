using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Blackhorse311.BotMind.Server;

public record BotMindModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.blackhorse311.botmind";
    public override string Name { get; init; } = "Blackhorse311-BotMind";
    public override string Author { get; init; } = "Blackhorse311";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new(1, 3, 0);
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader)]
public class BotMindMod(ILogger<BotMindMod> logger) : IOnLoad
{
    private const string ModName = "Blackhorse311-BotMind";

    public Task OnLoad()
    {
        logger.LogInformation($"{ModName}: Server mod loaded successfully!");
        return Task.CompletedTask;
    }
}
