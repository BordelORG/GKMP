using HarmonyLib;
using UnityEngine;
using GraveyardKeeperCoop.Multiplayer;

namespace GraveyardKeeperCoop.Patches
{
    /// <summary>
    /// Reports where an NPC is sent on a client, so NpcVisualSync can hand the move of a
    /// just-finished client scene's actors over to the host.
    /// </summary>
    [HarmonyPatch(typeof(MovementComponent), nameof(MovementComponent.GoTo), new System.Type[]
    {
        typeof(Vector2),
        typeof(bool),
        typeof(GJCommons.VoidDelegate),
        typeof(GJCommons.VoidDelegate),
        typeof(bool),
        typeof(MovementComponent.GoToMethod),
        typeof(string),
        typeof(System.Nullable<uint>),
        typeof(bool),
        typeof(GDPoint)
    })]
    internal static class NpcHandoffPatches
    {
        [HarmonyPostfix]
        private static void Postfix(MovementComponent __instance, Vector2 dest)
        {
            WorldGameObject wgo = __instance?.wgo;
            if (wgo == null || wgo.is_player) return;
            NpcVisualSync.Instance?.OnLocalGoTo(wgo, dest);
        }
    }
}
