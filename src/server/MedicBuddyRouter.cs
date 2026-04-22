using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Blackhorse311.BotMind.Server;

/// <summary>
/// Request payload for the MedicBuddy escort PMC generation endpoint.
/// </summary>
public record GenerateEscortRequest : IRequestData
{
    /// <summary>Number of PMC bots to generate (typically 4).</summary>
    public int Count { get; set; } = 4;
    /// <summary>Player faction: "usec" or "bear".</summary>
    public string Side { get; set; } = "usec";
    /// <summary>Bot difficulty: "easy", "normal", "hard", "impossible".</summary>
    public string Difficulty { get; set; } = "hard";
    /// <summary>Player level for gear scaling.</summary>
    public int PlayerLevel { get; set; } = 40;
    /// <summary>Map location ID (e.g., "Woods", "Customs").</summary>
    public string Location { get; set; } = "";
    /// <summary>Game version string for profile compatibility.</summary>
    public string GameVersion { get; set; } = "";
}

/// <summary>
/// Registers the /botmind/generate-escort endpoint for MedicBuddy PMC profile generation.
/// Uses SPT's BotController to generate real PMC profiles with equipment, skills, and appearance.
/// The client plugin calls this endpoint mid-raid to get profiles for ActivateBot().
/// </summary>
[Injectable]
public class MedicBuddyRouter : StaticRouter
{
    private static BotGenerator _botGenerator = null!;
    private static JsonUtil _jsonUtil = null!;
    private static ILogger<MedicBuddyRouter> _logger = null!;

    public MedicBuddyRouter(
        JsonUtil jsonUtil,
        BotGenerator botGenerator,
        ILogger<MedicBuddyRouter> logger
    ) : base(jsonUtil, GetRoutes())
    {
        _jsonUtil = jsonUtil;
        _botGenerator = botGenerator;
        _logger = logger;

        logger.LogInformation("[BotMind] MedicBuddy escort endpoint registered: /botmind/generate-escort");
    }

    private static List<RouteAction> GetRoutes()
    {
        return
        [
            new RouteAction<GenerateEscortRequest>(
                "/botmind/generate-escort",
                (url, request, sessionId, output) =>
                    GenerateEscortProfiles(request, sessionId)
            )
        ];
    }

    private static ValueTask<string> GenerateEscortProfiles(
        GenerateEscortRequest request, MongoId sessionId)
    {
      try
      {
            _logger.LogInformation(
                "[BotMind] Generating {Count} escort PMC profiles (side={Side}, diff={Diff}, level={Level})",
                request.Count, request.Side, request.Difficulty, request.PlayerLevel);

            var profiles = new List<object>();
            string role = request.Side.Equals("bear", StringComparison.OrdinalIgnoreCase)
                ? "pmcBEAR" : "pmcUSEC";
            string side = request.Side.Equals("bear", StringComparison.OrdinalIgnoreCase)
                ? "Bear" : "Usec";

            for (int i = 0; i < Math.Min(request.Count, 6); i++)
            {
                var details = new BotGenerationDetails
                {
                    IsPmc = true,
                    Side = side,
                    Role = role,
                    BotDifficulty = request.Difficulty,
                    BotCountToGenerate = 1,
                    PlayerLevel = request.PlayerLevel,
                    IsPlayerScav = false,
                    Location = request.Location,
                    GameVersion = request.GameVersion,
                };

                var bot = _botGenerator.PrepareAndGenerateBot(sessionId, details);

                if (bot != null)
                {
                    profiles.Add(bot);
                    _logger.LogDebug("[BotMind] Generated escort profile #{Index}: {Role}", i + 1, role);
                }
                else
                {
                    _logger.LogWarning("[BotMind] Failed to generate escort profile #{Index}", i + 1);
                }
            }

            _logger.LogInformation("[BotMind] Generated {Count}/{Requested} escort profiles",
                profiles.Count, request.Count);

            return new ValueTask<string>(_jsonUtil.Serialize(profiles));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BotMind] Failed to generate escort profiles");
            return new ValueTask<string>(_jsonUtil.Serialize(new List<object>()));
        }
    }
}
