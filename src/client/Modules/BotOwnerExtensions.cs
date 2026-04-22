using EFT;

namespace Blackhorse311.BotMind.Modules
{
    /// <summary>
    /// Extension methods for BotOwner to consolidate common operations.
    /// Prevents duplication of reset/cleanup logic across 10+ Stop() methods.
    /// </summary>
    public static class BotOwnerExtensions
    {
        /// <summary>
        /// Resets bot pose and movement speed to default standing values.
        /// Call in every logic/layer Stop() to prevent bots inheriting crouch/slow state.
        /// </summary>
        public static void ResetToDefaultStance(this BotOwner bot)
        {
            if (bot == null) return;
            bot.SetPose(1f);
            bot.SetTargetMoveSpeed(1f);
        }
    }
}
