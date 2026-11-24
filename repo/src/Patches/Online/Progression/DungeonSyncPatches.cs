using System;
using HarmonyLib;
using UnityEngine;
using DungeonGenerator;

namespace GraveyardKeeperCoop.Patches
{
    [HarmonyPatch]
    internal static class DungeonSyncPatches
    {
        [HarmonyPatch(typeof(MainGame), "TeleportToDungeonLevel", typeof(int))]
        [HarmonyPrefix]
        internal static void TeleportToDungeonLevel_Prefix(int level)
        {
            if (!IsSyncEnabled()) return;
            var sync = GraveyardKeeperCoop.Multiplayer.DungeonSync.Instance;
            if (sync == null) return;
            sync.SendDungeonEnter(level);
        }

        [HarmonyPatch(typeof(TextureDrawer), "TrySaveDungeon")]
        [HarmonyPostfix]
        internal static void TrySaveDungeon_Postfix(TextureDrawer __instance, bool __result)
        {
            if (!IsSyncEnabled()) return;
            if (!__result) return;
            if (__instance?.cur_dungeon_preset == null) return;
            var sync = GraveyardKeeperCoop.Multiplayer.DungeonSync.Instance;
            if (sync == null) return;
            sync.SendDungeonLevelState(__instance.cur_dungeon_preset.dungeon_level);
        }

        [HarmonyPatch(typeof(TextureDrawer), "UpdateDungeonState")]
        [HarmonyPostfix]
        internal static void UpdateDungeonState_Postfix(TextureDrawer __instance)
        {
            if (!IsSyncEnabled()) return;
            if (__instance?.cur_saved_dungeon == null || __instance?.cur_dungeon_preset == null) return;
            if (!__instance.cur_saved_dungeon.is_completed) return;
            var sync = GraveyardKeeperCoop.Multiplayer.DungeonSync.Instance;
            if (sync == null) return;
            sync.SendDungeonLevelCompleted(__instance.cur_dungeon_preset.dungeon_level);
        }

        // GKMP: every peer builds the dungeon itself from the shared seed. The rooms come
        // out the same, but the monsters did not: their kinds come from Unity's global
        // random state and their ids from the running id counter, both different on each
        // peer. Then the partner saw other monsters, and a kill on one side was not seen on
        // the other. While a level is generated, seed the random state from the level and
        // the shared seed and give out ids from a range reserved for that level; restore
        // both afterwards.
        private const long DungeonUidBase = 2000000000000L;
        private const long DungeonUidPerLevel = 1000000L;
        private static readonly System.Reflection.FieldInfo UniqueIdCounter =
            AccessTools.Field(typeof(UniqueID), "_unique_id");

        private static bool generating;
        private static int generatingSeed;

        // The generation's other random draws are not the same count on every peer, so each
        // spawner draws its monster from a state of its own (shared seed + its position).
        [HarmonyPatch(typeof(MobSpawner), "ActivateSpawner", typeof(int), typeof(System.Collections.Generic.List<SavedDungeonObject>))]
        [HarmonyPrefix]
        internal static void ActivateSpawner_Prefix(MobSpawner __instance)
        {
            if (!generating || __instance == null)
                return;
            Vector2 p = __instance.transform.localPosition;
            UnityEngine.Random.InitState(unchecked(generatingSeed * 486187739 +
                Mathf.RoundToInt(p.x) * 73856093 + Mathf.RoundToInt(p.y) * 19349663));
        }

        internal struct GenerationState
        {
            public bool Active;
            public UnityEngine.Random.State Random;
            public long UniqueId;
            public int Level;
        }

        [HarmonyPatch(typeof(TextureDrawer), "DrawTexture")]
        [HarmonyPrefix]
        internal static void DrawTexture_Prefix(DungeonPreset dungeon_preset, out GenerationState __state)
        {
            __state = default(GenerationState);
            if (!IsSyncEnabled() || dungeon_preset == null || MainGame.me?.save == null || UniqueIdCounter == null)
                return;
            int level = dungeon_preset.dungeon_level;
            int seed = MainGame.me.save.dungeon_seed;
            __state.Active = true;
            __state.Random = UnityEngine.Random.state;
            __state.UniqueId = (long)UniqueIdCounter.GetValue(null);
            UnityEngine.Random.InitState(unchecked(seed * 7919 + level * 104729 + 17));
            generatingSeed = unchecked(seed * 31 + level);
            generating = true;
            UniqueIdCounter.SetValue(null, DungeonUidBase + Math.Max(0, level) * DungeonUidPerLevel);
            __state.Level = level;
            // A fresh level (the host enters it, so both build it from the host's saved state):
            // forget the kills of the last copy. A partner rebuilding it alone (e.g. after dying)
            // keeps them and removes those monsters again below.
            bool isHost = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance?.IsHost ?? true;
            if (isHost || hostGeneratedLevel == level || killedLevel != level)
            {
                killed.Clear();
                killedLevel = level;
                hostGeneratedLevel = -1;
            }
            CoopMod.Logger.LogInfo($"[DungeonSync] Generating level {level} (seed {seed}) with shared random state and ids");
        }

        private static readonly System.Collections.Generic.HashSet<long> killed = new System.Collections.Generic.HashSet<long>();
        private static int killedLevel = -1;
        private static int hostGeneratedLevel = -1;

        internal static void OnHostGeneratesLevel(int level) => hostGeneratedLevel = level;

        // Monsters and objects of the current level removed in play (kills, broken barrels).
        [HarmonyPatch(typeof(WorldGameObject), nameof(WorldGameObject.DestroyMe))]
        [HarmonyPrefix]
        internal static void DestroyMe_Prefix(WorldGameObject __instance)
        {
            if (__instance != null && !generating && IsDungeonUid(__instance.unique_id))
                killed.Add(__instance.unique_id);
        }

        // The partner keeps the host's saved state of the level: its own save (on death or when
        // following the host out) would change which spawners fire, and so every id after them.
        [HarmonyPatch(typeof(TextureDrawer), "TrySaveDungeon")]
        [HarmonyPrefix]
        internal static bool TrySaveDungeon_Prefix(ref bool __result)
        {
            var ocm = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            if (!IsSyncEnabled() || ocm == null || ocm.IsHost) return true;
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(TextureDrawer), "DrawTexture")]
        [HarmonyFinalizer]
        internal static Exception DrawTexture_Finalizer(Exception __exception, GenerationState __state)
        {
            if (__state.Active)
            {
                generating = false;
                UnityEngine.Random.state = __state.Random;
                UniqueIdCounter.SetValue(null, __state.UniqueId);
                if (killed.Count > 0 && killedLevel == __state.Level && MainGame.me?.dungeon_root != null)
                {
                    int removed = 0;
                    foreach (var wgo in MainGame.me.dungeon_root.GetComponentsInChildren<WorldGameObject>(true))
                    {
                        if (wgo == null || !killed.Contains(wgo.unique_id)) continue;
                        wgo.DestroyMe();
                        removed++;
                    }
                    CoopMod.Logger.LogInfo($"[DungeonSync] Rebuilt level {__state.Level} alone: removed {removed} monsters/objects destroyed earlier");
                }
            }
            return __exception;
        }

        private static readonly System.Reflection.FieldInfo WorldSpawnersField =
            AccessTools.Field(typeof(WorldMap), "_world_spawners");

        // Dungeon spawners stay in WorldMap._world_spawners after the level is destroyed. Vanilla
        // rarely deserializes mob idle data later, but synced mobs do, and the lookup then throws
        // on the destroyed spawner. Drop destroyed entries before the lookup.
        [HarmonyPatch(typeof(WorldMap), "GetSpawnerByCoords")]
        [HarmonyPrefix]
        internal static void GetSpawnerByCoords_Prefix()
        {
            if (WorldSpawnersField?.GetValue(null) is System.Collections.Generic.List<MobSpawner> spawners)
                spawners.RemoveAll(s => s == null);
        }

        private static readonly System.Reflection.FieldInfo WorldObjsField = AccessTools.Field(typeof(WorldMap), "_objs");
        private static readonly System.Reflection.FieldInfo WorldObjIdsField = AccessTools.Field(typeof(WorldMap), "_objs_ids");

        internal static bool IsDungeonUid(long uid) => uid >= DungeonUidBase && uid < DungeonUidBase + 1000L * DungeonUidPerLevel;

        // Dungeon ids are deterministic, so a regenerated level reuses the ids of the previous copy.
        // The old copy is destroyed with Object.Destroy (end of frame), so when the new copy registers
        // in the same frame the id is still taken and vanilla skips it: the new mob never enters
        // WorldMap.objs. Hand the id over to the new object instead.
        [HarmonyPatch(typeof(WorldMap), "OnAddNewWGO")]
        [HarmonyPrefix]
        internal static void OnAddNewWGO_Prefix(WorldGameObject wgo)
        {
            if (wgo == null || !IsDungeonUid(wgo.unique_id)) return;
            if (!(WorldObjsField?.GetValue(null) is System.Collections.Generic.List<WorldGameObject> objs) ||
                !(WorldObjIdsField?.GetValue(null) is System.Collections.Generic.HashSet<long> ids) ||
                !ids.Contains(wgo.unique_id) || objs.Contains(wgo)) return;
            long uid = wgo.unique_id;
            objs.RemoveAll(o => o == null || (o != wgo && o.unique_id == uid));
            ids.Remove(uid); // vanilla adds it back together with the new object
        }

        // The old copy's OnDestroy must not unregister the id the new copy now owns.
        [HarmonyPatch(typeof(WorldMap), "OnDestroyWGO")]
        [HarmonyPrefix]
        internal static bool OnDestroyWGO_Prefix(WorldGameObject wgo)
        {
            if (wgo == null || !IsDungeonUid(wgo.unique_id)) return true;
            if (!(WorldObjsField?.GetValue(null) is System.Collections.Generic.List<WorldGameObject> objs) || objs.Contains(wgo))
                return true;
            long uid = wgo.unique_id;
            if (!objs.Exists(o => o != null && o != wgo && o.unique_id == uid)) return true;
            foreach (var point in wgo.GetComponentsInChildren<GDPoint>(true))
                WorldMap.gd_points.Remove(point);
            return false;
        }

        private static bool IsSyncEnabled()
        {
            var onlineCoop = GraveyardKeeperCoop.Network.OnlineCoopManager.Instance;
            return onlineCoop != null && onlineCoop.IsOnlineCoopEnabled;
        }
    }
}