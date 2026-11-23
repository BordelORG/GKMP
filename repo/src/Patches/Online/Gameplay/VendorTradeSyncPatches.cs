using HarmonyLib;

namespace GraveyardKeeperCoop.Patches
{
    /// <summary>
    /// GKMP: a finished trade changes the vendor's stock and money. Mark the vendor as changed
    /// right away so the partner gets it even when the vendor only just became active around
    /// this player (the periodic scan would otherwise take its new state as the baseline).
    /// </summary>
    [HarmonyPatch]
    internal static class VendorTradeSyncPatches
    {
        [HarmonyPatch(typeof(Trading), nameof(Trading.DoAcceptOffer))]
        [HarmonyPostfix]
        internal static void DoAcceptOffer_Postfix(Trading __instance)
        {
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            if (onlineCoop == null || !onlineCoop.IsOnlineCoopEnabled || __instance?.trader?.vendor_data == null)
                return;
            GraveyardKeeperCoop.Multiplayer.InventorySync.Instance?.NotifyInventoryDataMutated(
                __instance.trader.vendor_data, force: true);
        }
    }
}
