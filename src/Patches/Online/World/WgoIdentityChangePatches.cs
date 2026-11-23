using HarmonyLib;

namespace GraveyardKeeperCoop.Patches
{
    /// <summary>
    /// Records direct identity changes of world objects (replaced, destroyed, killed) so a
    /// world-state snapshot captured before the change cannot revive the old object.
    /// </summary>
    [HarmonyPatch]
    internal static class WgoIdentityChangePatches
    {
        [HarmonyPatch(typeof(WorldGameObject), nameof(WorldGameObject.ReplaceWithObject))]
        [HarmonyPrefix]
        internal static void ReplaceWithObject_Prefix(WorldGameObject __instance) => Note(__instance);

        [HarmonyPatch(typeof(WorldGameObject), nameof(WorldGameObject.DestroyMe))]
        [HarmonyPrefix]
        internal static void DestroyMe_Prefix(WorldGameObject __instance) => Note(__instance);

        [HarmonyPatch(typeof(WorldGameObject), "DoPreZeroHPActivity")]
        [HarmonyPrefix]
        internal static void DoPreZeroHPActivity_Prefix(WorldGameObject __instance) => Note(__instance);

        private static void Note(WorldGameObject wgo)
        {
            if (wgo == null || wgo.is_player) return;
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            if (onlineCoop == null || !onlineCoop.IsOnlineCoopEnabled) return;
            GraveyardKeeperCoop.Multiplayer.WGOStateSync.NoteIdentityChange(wgo.unique_id);
        }
    }
}
