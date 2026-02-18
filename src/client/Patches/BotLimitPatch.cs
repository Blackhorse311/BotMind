using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace Blackhorse311.BotMind.Patches
{
    /// <summary>
    /// Harmony PREFIX patch on BotsController.SetSettings to override the max bot count.
    ///
    /// When the MaxBotsPerMap slider is > 0, this patch replaces the incoming maxCount
    /// parameter with EffectiveMaxBots (slider value minus reserved MedicBuddy slots).
    /// When slider is 0 (default), the patch is a no-op and the game default is used.
    ///
    /// Compatible with Donuts' POSTFIX on the same method (Donuts doesn't modify maxCount).
    /// </summary>
    internal class BotLimitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsController), nameof(BotsController.SetSettings));
        }

        [PatchPrefix]
        private static void PatchPrefix(ref int maxCount)
        {
            try
            {
                int effectiveMax = BotLimitManager.EffectiveMaxBots;
                if (effectiveMax <= 0) return; // slider=0, use game default

                int originalMax = maxCount;
                maxCount = effectiveMax;

                BotMindPlugin.Log?.LogInfo(
                    $"[BotLimitPatch] Bot limit override: game={originalMax} -> effective={effectiveMax} " +
                    $"(slider={BotLimitManager.SliderValue}, reserved={BotLimitManager.ReservedSlots})");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError(
                    $"[BotLimitPatch] Error in PREFIX: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
