using System.Collections.Generic;
using EFT.InventoryLogic;

namespace Blackhorse311.BotMind.Modules
{
    /// <summary>
    /// Shared utility for bot inventory operations. Consolidates the container
    /// gathering pattern duplicated across 6+ classes (LootContainerLogic,
    /// LootCorpseLogic, PickupItemLogic, FindItemLogic, PlaceItemLogic, LootFinder).
    /// </summary>
    public static class BotInventoryHelper
    {
        /// <summary>
        /// Gathers the bot's equipment containers (vest, backpack, pockets) into the output list.
        /// Clears the list before populating. Skips null slots/items.
        /// </summary>
        public static void GetEquipmentContainers(InventoryEquipment equipment, List<CompoundItem> output)
        {
            output.Clear();
            if (equipment == null) return;

            AddContainerIfNotNull(equipment, EquipmentSlot.TacticalVest, output);
            AddContainerIfNotNull(equipment, EquipmentSlot.Backpack, output);
            AddContainerIfNotNull(equipment, EquipmentSlot.Pockets, output);
        }

        private static void AddContainerIfNotNull(InventoryEquipment equipment, EquipmentSlot slot, List<CompoundItem> output)
        {
            var item = equipment.GetSlot(slot)?.ContainedItem as CompoundItem;
            if (item != null)
            {
                output.Add(item);
            }
        }
    }
}
