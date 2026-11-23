using System.Collections.Generic;
using GraveyardKeeperCoop.Network;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using System.Linq;

namespace CoopQA
{
    /// <summary>Test-only counters on the mod's position path (read-only postfixes/prefixes).</summary>
    internal static class Diag
    {
        public static int PosSent, PosReceived, PosSnaps;
        public static Vector3 LastSent, LastReceived;
        public static string LastSnapReason = "";
        public static readonly List<string> Notes = new List<string>();

        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(SteamP2PManager), "SendPositionWithState"),
                prefix: new HarmonyMethod(typeof(Diag), nameof(Sent)));
            h.Patch(AccessTools.Method(typeof(OnlineCoopManager), "OnRemotePlayerPositionReceived"),
                prefix: new HarmonyMethod(typeof(Diag), nameof(Received)));
            h.Patch(AccessTools.Method(typeof(CraftComponent), "Craft"),
                postfix: new HarmonyMethod(typeof(Diag), nameof(CraftDone)));
            h.Patch(AccessTools.Method(typeof(WorldMap), "OnAddNewWGO"),
                prefix: new HarmonyMethod(typeof(Diag), nameof(Added)));
            h.Patch(AccessTools.Method(typeof(WorldMap), "OnAddNewWGO"),
                postfix: new HarmonyMethod(typeof(Diag), nameof(AddedTwice)));
            h.Patch(AccessTools.Method(typeof(WorldMap), "OnDestroyWGO"),
                prefix: new HarmonyMethod(typeof(Diag), nameof(Unlisted)));
            h.Patch(AccessTools.Method(typeof(OnlineCoopManager), "SnapRemotePlayerToNetworkPosition"),
                prefix: new HarmonyMethod(typeof(Diag), nameof(Snapped)));
        }

        private static readonly System.Reflection.FieldInfo ObjsF = AccessTools.Field(typeof(WorldMap), "_objs");
        private static int dupNotes;

        /// <summary>An object listed twice in WorldMap: log who added it (first few times).</summary>
        private static void AddedTwice(WorldGameObject wgo)
        {
            if (wgo == null || dupNotes >= 3) return;
            var objs = ObjsF.GetValue(null) as List<WorldGameObject>;
            if (objs == null || objs.Count(o => o == wgo) < 2) return;
            dupNotes++;
            Debug.Log($"[CoopQA] DUP {wgo.obj_id}#{wgo.unique_id} listed twice in WorldMap after OnAddNewWGO:\n{System.Environment.StackTrace}");
        }

        private static void Unlisted(WorldGameObject wgo)
        {
            if (wgo == null || wgo.obj_id != "dungeon_exit") return;
            Debug.Log($"[CoopQA] UNLIST {wgo.obj_id}#{wgo.unique_id}:\n{System.Environment.StackTrace}");
        }

        /// <summary>Unique ids of destroyed objects; registering one again is logged with its call stack.</summary>
        public static readonly HashSet<long> Destroyed = new HashSet<long>();
        public static readonly List<string> Revived = new List<string>();

        private static void Added(WorldGameObject wgo)
        {
            if (wgo == null || !Destroyed.Contains(wgo.unique_id)) return;
            string note = $"{wgo.obj_id}#{wgo.unique_id} hp={wgo.hp} registered again:\n{System.Environment.StackTrace}";
            Revived.Add(note);
            Plugin.Log.LogWarning("[CoopQA] REVIVED " + note);
        }

        private static void Sent(Vector3 position) { PosSent++; LastSent = position; }
        private static void Received(CSteamID senderID, Vector3 position) { PosReceived++; LastReceived = position; }
        private static void Snapped(Vector3 position, string reason) { PosSnaps++; LastSnapReason = reason; }

        private static void CraftDone(CraftComponent __instance, CraftDefinition craft, bool __result) =>
            Plugin.Log.LogInfo($"[CoopQA] Craft('{craft?.id}') on {__instance?.wgo?.obj_id}#{__instance?.wgo?.unique_id} -> {__result} is_crafting={__instance?.is_crafting} " +
                               "needs: " + string.Join(",", craft?.needs?.Select(n => $"{n.id} have {MainGame.me.player.data.GetTotalCount(n.id, true)}/{n.value}") ?? new string[0]));

        public static string Summary() =>
            $"pos sent={PosSent} last=({LastSent.x:F1},{LastSent.y:F1}) received={PosReceived} last=({LastReceived.x:F1},{LastReceived.y:F1}) snaps={PosSnaps} '{LastSnapReason}'";
    }
}
