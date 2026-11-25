using System;
using System.Collections.Generic;
using HarmonyLib;
using GraveyardKeeperCoop.Multiplayer;
using UnityEngine;

namespace GraveyardKeeperCoop.Patches
{
    [HarmonyPatch]
    internal static class InventoryMutationSyncPatches
    {
        [HarmonyPatch(typeof(WorldGameObject), "AddToInventory", new Type[] { typeof(Item) })]
        [HarmonyPostfix]
        private static void WorldGameObject_AddToInventory_Postfix(
            WorldGameObject __instance,
            Item item,
            bool __result)
        {
            if (!__result || InventorySync.IsSuppressingMutationCapture)
                return;

            bool isBody =
                item?.definition?.type == ItemDefinition.ItemType.Body;
            if (isBody)
            {
                CoopMod.Logger.LogInfo(
                    $"[InventorySync] Body inserted into {__instance?.obj_id ?? "unknown"}; sending authoritative inventory immediately");
            }
            InventorySync.Instance?.NotifyInventoryMutated(
                __instance,
                force: isBody);
            if (__instance == MainGame.me?.player)
            {
                JoinerProfileManager.NotifyPersonalInventoryChanged(
                    $"picked up {item?.id ?? "item"}");
            }
        }

        /// <summary>A script adding to an object's own params (a sermon visitor dropping
        /// money in the donation box) is a change like an item moving. On a client it is
        /// marked so a host snapshot that predates it cannot overwrite it before it is
        /// sent; on the host it is sent even when the object is outside the active chunks
        /// (the box usually is while the preacher stands at the pulpit, so the partner
        /// saw the donations only much later). Players and NPCs are skipped (their params
        /// churn constantly and are synced by their own systems).</summary>
        [HarmonyPatch(typeof(WorldGameObject), nameof(WorldGameObject.AddToParams), new Type[] { typeof(string), typeof(float) })]
        [HarmonyPostfix]
        private static void WorldGameObject_AddToParams_Postfix(
            WorldGameObject __instance,
            float value)
        {
            if (value == 0f || __instance == null || __instance.is_player ||
                InventorySync.IsSuppressingMutationCapture)
            {
                return;
            }
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            if (onlineCoop == null || !onlineCoop.IsOnlineCoopEnabled)
                return;
            if (__instance.obj_def == null || __instance.obj_def.IsNPC())
                return;
            InventorySync.Instance?.NotifyInventoryMutated(__instance);
        }

        /// <summary>Porters run on the host only. The station's state reaches the client by
        /// sync, and a client seeing "waiting" with a docked zombie would load its own copy
        /// of the source storages and deliver the same goods a second time.</summary>
        [HarmonyPatch(typeof(PorterStation), "TrySendPorter")]
        [HarmonyPrefix]
        private static bool PorterStation_TrySendPorter_Prefix(ref bool __result)
        {
            if (!IsOnlineClient()) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(PorterStation), nameof(PorterStation.OnCameToDestination))]
        [HarmonyPrefix]
        private static bool PorterStation_OnCameToDestination_Prefix() => !IsOnlineClient();

        [HarmonyPatch(typeof(PorterStation), nameof(PorterStation.OnCameToSource))]
        [HarmonyPrefix]
        private static bool PorterStation_OnCameToSource_Prefix() => !IsOnlineClient();

        private static bool IsOnlineClient()
        {
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            return onlineCoop != null && onlineCoop.IsOnlineCoopEnabled && !onlineCoop.IsHost;
        }

        /// <summary>At the end of each day every vendor trades with the "bank": it earns
        /// its daily income and restocks or sells off goods, with random amounts. Both
        /// peers ran it on their own, so a vendor ended up with different stock and money
        /// on each side (and a purchase made just before was undone). The host runs it and
        /// sends each vendor's stock and money; a client skips it.</summary>
        [HarmonyPatch(typeof(WorldMap), nameof(WorldMap.VendorsTradeWithBank))]
        [HarmonyPrefix]
        private static bool WorldMap_VendorsTradeWithBank_Prefix()
        {
            return !IsOnlineClient();
        }

        [HarmonyPatch(typeof(WorldMap), nameof(WorldMap.VendorsTradeWithBank))]
        [HarmonyPostfix]
        private static void WorldMap_VendorsTradeWithBank_Postfix()
        {
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            if (onlineCoop == null || !onlineCoop.IsOnlineCoopEnabled || !onlineCoop.IsHost ||
                InventorySync.Instance == null || WorldMap.objs == null)
                return;
            int reported = 0;
            for (int i = 0; i < WorldMap.objs.Count; i++)
            {
                WorldGameObject wgo = WorldMap.objs[i];
                if (wgo == null || wgo.is_removed || wgo.vendor == null)
                    continue;
                InventorySync.Instance.NotifyInventoryMutated(wgo);
                reported++;
            }
            CoopMod.Logger.LogInfo($"[InventorySync] End of day: sending {reported} vendors' stock and money");
        }

        /// <summary>A porter loads its backpack by editing the source storages' item lists
        /// directly (no AddItem/RemoveItem), so report those storages after it sets off.</summary>
        [HarmonyPatch(typeof(PorterStation), "TrySendPorter")]
        [HarmonyPostfix]
        private static void PorterStation_TrySendPorter_Postfix(PorterStation __instance, bool __result)
        {
            if (!__result || InventorySync.Instance == null)
                return;
            try
            {
                var inventories = __instance.source?.GetMultiInventory();
                if (inventories == null)
                    return;
                for (int i = 0; i < inventories.Count; i++)
                {
                    WorldGameObject owner = InventorySync.FindWgoByData(inventories[i]?.data);
                    if (owner != null)
                        InventorySync.Instance.NotifyInventoryMutated(owner);
                }
            }
            catch (Exception e)
            {
                CoopMod.Logger.LogWarning($"[InventorySync] Porter source report failed: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(Item), "AddItem", new Type[]
        {
            typeof(Item),
            typeof(bool)
        })]
        [HarmonyPostfix]
        private static void Item_AddItem_Postfix(
            Item __instance,
            Item item,
            bool __result)
        {
            if (!__result || InventorySync.IsSuppressingMutationCapture)
                return;

            InventorySync.Instance?.NotifyInventoryDataMutated(__instance);
            if (JoinerProfileManager.IsOwnedByLocalPlayer(__instance))
            {
                JoinerProfileManager.NotifyPersonalInventoryChanged(
                    $"added {item?.id ?? "item"}");
            }
        }

        [HarmonyPatch(typeof(WorldGameObject), "GiveItemToPlayersHands", new Type[] { typeof(Item) })]
        [HarmonyPostfix]
        private static void WorldGameObject_GiveItemToPlayersHands_Postfix(
            WorldGameObject __instance,
            Item item)
        {
            if (InventorySync.IsSuppressingMutationCapture ||
                item?.definition?.type != ItemDefinition.ItemType.Body)
            {
                return;
            }

            int duplicateBodiesRemoved = 0;
            if (__instance != null &&
                __instance.is_autopsy_table &&
                __instance.data?.inventory != null)
            {
                for (int i = __instance.data.inventory.Count - 1; i >= 0; i--)
                {
                    Item remaining = __instance.data.inventory[i];
                    if (remaining?.definition?.type !=
                        ItemDefinition.ItemType.Body)
                    {
                        continue;
                    }

                    __instance.data.inventory.RemoveAt(i);
                    duplicateBodiesRemoved++;
                }
            }

            CoopMod.Logger.LogInfo(
                $"[InventorySync] Body removed from {__instance?.obj_id ?? "unknown"}; " +
                $"cleared_duplicate_bodies={duplicateBodiesRemoved}, sending authoritative inventory immediately");
            InventorySync.Instance?.NotifyInventoryMutated(
                __instance,
                force: true);
        }

        [HarmonyPatch(typeof(Item), "RemoveItem", new Type[] { typeof(string), typeof(int), typeof(Item) })]
        [HarmonyPostfix]
        private static void Item_RemoveItemById_Postfix(Item __instance, bool __result)
        {
            if (__result && !InventorySync.IsSuppressingMutationCapture)
            {
                InventorySync.Instance?.NotifyInventoryDataMutated(__instance);
                NotifyPersonalRemoval(__instance);
            }
        }

        [HarmonyPatch(typeof(Item), "RemoveItem", new Type[] { typeof(Item), typeof(int), typeof(Item) })]
        [HarmonyPostfix]
        private static void Item_RemoveItemByItem_Postfix(Item __instance, bool __result)
        {
            if (__result && !InventorySync.IsSuppressingMutationCapture)
            {
                InventorySync.Instance?.NotifyInventoryDataMutated(__instance);
                NotifyPersonalRemoval(__instance);
            }
        }

        [HarmonyPatch(typeof(Item), "RemoveItems", new Type[] { typeof(List<Item>), typeof(int) })]
        [HarmonyPostfix]
        private static void Item_RemoveItems_Postfix(Item __instance, bool __result)
        {
            if (__result && !InventorySync.IsSuppressingMutationCapture)
            {
                InventorySync.Instance?.NotifyInventoryDataMutated(__instance);
                NotifyPersonalRemoval(__instance);
            }
        }

        [HarmonyPatch(typeof(Item), "RemoveItemByIndex", new Type[] { typeof(int) })]
        [HarmonyPostfix]
        private static void Item_RemoveItemByIndex_Postfix(Item __instance, bool __result)
        {
            if (__result && !InventorySync.IsSuppressingMutationCapture)
            {
                InventorySync.Instance?.NotifyInventoryDataMutated(__instance);
                NotifyPersonalRemoval(__instance);
            }
        }

        [HarmonyPatch(typeof(Item), "RemoveNotFoldedItem", new Type[] { typeof(Item) })]
        [HarmonyPostfix]
        private static void Item_RemoveNotFoldedItem_Postfix(Item __instance)
        {
            if (!InventorySync.IsSuppressingMutationCapture)
            {
                InventorySync.Instance?.NotifyInventoryDataMutated(__instance);
                NotifyPersonalRemoval(__instance);
            }
        }

        private static void NotifyPersonalRemoval(Item owner)
        {
            if (JoinerProfileManager.IsOwnedByLocalPlayer(owner))
            {
                JoinerProfileManager.NotifyPersonalInventoryChanged(
                    "removed personal inventory item");
            }
        }

        [HarmonyPatch(typeof(InventoryPanelGUI), "Hide")]
        [HarmonyPrefix]
        private static void InventoryPanelGUI_Hide_Prefix()
        {
            InventorySync.Instance?.FlushNow();
        }
    }

    [HarmonyPatch(
        typeof(WorldGameObject),
        nameof(WorldGameObject.UseItemFromInventory),
        new Type[]
        {
            typeof(Item),
            typeof(Nullable<Vector3>),
            typeof(Item)
        })]
    internal static class PersonalConsumableUsePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            WorldGameObject __instance,
            Item item,
            Vector3? effect_bubble_pos,
            Item use_from_bag,
            ref GameRes __result)
        {
            if (InventorySync.Instance?.TryDeferPersonalConsumableUse(
                    __instance,
                    item,
                    effect_bubble_pos,
                    use_from_bag) != true)
            {
                return true;
            }

            __result = new GameRes();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class PersonalBuffUsagePatches
    {
        [HarmonyPatch(typeof(BuffsLogics), nameof(BuffsLogics.AddBuff))]
        [HarmonyPrefix]
        private static bool AddBuffPrefix(string buff_id)
        {
            return AllowPersonalBuffMutation("add", buff_id);
        }

        [HarmonyPatch(typeof(BuffsLogics), nameof(BuffsLogics.RemoveBuff))]
        [HarmonyPrefix]
        private static bool RemoveBuffPrefix(string buff_id)
        {
            return AllowPersonalBuffMutation("remove", buff_id);
        }

        private static bool AllowPersonalBuffMutation(
            string operation,
            string buffId)
        {
            if (!CutsceneSyncPatches.IsApplyingRemotePresentation)
                return true;

            CoopMod.Logger.LogInfo(
                $"[PersonalBuff] Suppressed remote-presentation {operation} " +
                $"for personal buff '{buffId ?? "unknown"}'");
            return false;
        }
    }


    [HarmonyPatch(typeof(ChestGUI), "MoveItem", new Type[]
    {
        typeof(Item),
        typeof(int),
        typeof(bool),
        typeof(Item),
        typeof(bool)
    })]
    internal static class ChestGuiMoveItemOperationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            ChestGUI __instance,
            WorldGameObject ____chest_obj,
            Item item,
            int count,
            bool to_chest,
            bool after_item_count_gui,
            out InventorySync.ChestMoveCapture __state)
        {
            InventorySync sync = InventorySync.Instance;
            if (sync == null)
            {
                __state = null;
                return true;
            }

            return sync.TryBeginChestMove(
                __instance,
                ____chest_obj,
                item,
                count,
                to_chest,
                after_item_count_gui,
                out __state);
        }

        [HarmonyPostfix]
        private static void Postfix(InventorySync.ChestMoveCapture __state)
        {
            InventorySync.Instance?.CompleteChestMove(__state);
        }
    }
}
