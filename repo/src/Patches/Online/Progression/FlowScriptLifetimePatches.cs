using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GraveyardKeeperCoop.Patches
{
    /// <summary>
    /// Remembers when each FlowScript started, so the stuck-control watchdog can tell a
    /// player locked by a scene that is still playing (an NPC walking a long way before
    /// anyone speaks) from a lock nobody will ever release.
    /// </summary>
    [HarmonyPatch]
    internal static class FlowScriptLifetimePatches
    {
        private const int MaxTracked = 64;
        private static readonly List<KeyValuePair<CustomFlowScript, float>> started =
            new List<KeyValuePair<CustomFlowScript, float>>();

        [HarmonyPatch(typeof(CustomFlowScript), nameof(CustomFlowScript.Create),
            new[] { typeof(GameObject), typeof(FlowCanvas.FlowGraph), typeof(bool), typeof(CustomFlowScript.OnFinishedDelegate), typeof(string) })]
        [HarmonyPostfix]
        private static void Create_Postfix(CustomFlowScript __result)
        {
            if (__result == null) return;
            Prune();
            if (started.Count >= MaxTracked) started.RemoveAt(0);
            started.Add(new KeyValuePair<CustomFlowScript, float>(__result, Time.realtimeSinceStartup));
        }

        /// <summary>The name of a FlowScript that started at or after <paramref name="since"/>
        /// and is still running, or null.</summary>
        internal static string RunningScriptStartedSince(float since)
        {
            Prune();
            for (int i = started.Count - 1; i >= 0; i--)
            {
                if (started[i].Value >= since) return started[i].Key.script_name;
            }
            return null;
        }

        private static void Prune()
        {
            // A finished script destroys its GameObject (CustomScript.TerminateMe).
            started.RemoveAll(e => e.Key == null || e.Key.gameObject == null);
        }
    }
}
