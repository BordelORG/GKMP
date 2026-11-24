using HarmonyLib;

namespace GraveyardKeeperCoop.Patches
{
    [HarmonyPatch]
    internal static class WorkerSyncPatches
    {
        [HarmonyPatch(typeof(SavedWorkersList), "CreateNewWorker")]
        [HarmonyPostfix]
        internal static void CreateNewWorker_Postfix(SavedWorkersList __instance, Worker __result)
        {
            if (!IsSyncEnabled()) return;
            if (__result == null) return;

            var sync = GraveyardKeeperCoop.Multiplayer.WorkerSync.Instance;
            if (sync == null) return;
            sync.SendWorkerCreated(__result);
        }

        [HarmonyPatch(typeof(WorldGameObject), nameof(WorldGameObject.linked_worker), MethodType.Setter)]
        [HarmonyPrefix]
        internal static void LinkedWorker_Prefix(WorldGameObject __instance, out WorldGameObject __state)
        {
            __state = __instance != null ? __instance.linked_worker : null;
        }

        [HarmonyPatch(typeof(WorldGameObject), nameof(WorldGameObject.linked_worker), MethodType.Setter)]
        [HarmonyPostfix]
        internal static void LinkedWorker_Postfix(WorldGameObject __instance, WorldGameObject __state)
        {
            if (!IsSyncEnabled() || __instance == null) return;
            if (__state == __instance.linked_worker) return;
            GraveyardKeeperCoop.Multiplayer.WorkerSync.Instance?.NoteLinkChanged(__instance, __state);
        }

        private static bool IsSyncEnabled()
        {
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            return onlineCoop != null && onlineCoop.IsOnlineCoopEnabled;
        }
    }
}