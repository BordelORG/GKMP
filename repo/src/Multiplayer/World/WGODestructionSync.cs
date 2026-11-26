using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Steamworks;
using GraveyardKeeperCoop.Network;

namespace GraveyardKeeperCoop.Multiplayer
{
    /// <summary>
    /// Synchronizes WorldGameObject destruction between host and clients.
    /// When a WGO is destroyed:
    /// - Client sends destruction request to host
    /// - Host broadcasts destruction to all clients
    /// - All clients destroy the WGO locally
    /// </summary>
    public class WGODestructionSync : SyncBehaviour
    {
        public static WGODestructionSync Instance => GetInstance<WGODestructionSync>();

        private const int MaxLocalDestroyBroadcastsPerWindow = 6;
        private const float LocalDestroyBurstWindowSeconds = 1f;
        private const float LocalDestroyBurstSuppressSeconds = 6f;

        private static bool isProcessingRemoteDestroy = false;
        private readonly Queue<float> recentLocalDestroyBroadcasts = new Queue<float>();
        private float suppressLocalDestroyBroadcastUntil;

        // A burst held back (a scene despawning its eight spectators at once) is sent
        // afterwards, a few at a time, instead of being dropped: a dropped destroy leaves
        // the object on the other side forever. A client's goes to the host as usual.
        private const int MaxDeferredDestroys = 512;
        private readonly List<long> deferredDestroys = new List<long>();

        public static bool IsProcessingRemote => isProcessingRemoteDestroy || WGOStateSync.IsProcessingRemote;

        protected override string LogPrefix => "[WGODestructionSync]";

        protected override void SubscribeEvents()
        {
            if (SteamP2PManager.Instance != null)
            {
                SteamP2PManager.Instance.OnWGODestroyReceived -= OnWGODestroyReceived;
                SteamP2PManager.Instance.OnWGODestroyReceived += OnWGODestroyReceived;
            }
        }

        protected override void UnsubscribeEvents()
        {
            if (SteamP2PManager.Instance != null)
            {
                SteamP2PManager.Instance.OnWGODestroyReceived -= OnWGODestroyReceived;
            }
        }

        protected override void OnSyncEnabled()
        {
            recentLocalDestroyBroadcasts.Clear();
            suppressLocalDestroyBroadcastUntil = 0f;
            deferredDestroys.Clear();
        }

        protected override void OnSyncDisabled()
        {
            recentLocalDestroyBroadcasts.Clear();
            suppressLocalDestroyBroadcastUntil = 0f;
            deferredDestroys.Clear();
        }

        public void OnLocalWGODestroyed(long uniqueId)
        {
            if (!IsSyncEnabled) return;
            if (isProcessingRemoteDestroy) return;
            if (!IsOnline) return;

            if (!TryAllowLocalDestroyBroadcast(uniqueId))
            {
                // Only a scene's own clean-up is replayed (its script despawning the
                // actors it spawned). Other bursts, like a dungeon level torn down on
                // one side while the partner is still in it, must stay local.
                if (IsLocalSceneRunning() &&
                    deferredDestroys.Count < MaxDeferredDestroys &&
                    !deferredDestroys.Contains(uniqueId))
                {
                    deferredDestroys.Add(uniqueId);
                }
                return;
            }

            if (IsHost)
            {
                SteamP2PManager.Instance?.BroadcastWGODestroy(uniqueId, null);
                CoopMod.Logger.LogDebug($"{LogPrefix} Host broadcasting destroy: {uniqueId}");
            }
            else
            {
                SteamP2PManager.Instance?.SendWGODestroy(uniqueId, toHost: true);
                CoopMod.Logger.LogDebug($"{LogPrefix} Client sending destroy to host: {uniqueId}");
            }
        }

        private static bool IsLocalSceneRunning() =>
            Patches.CutsceneSyncPatches.HasLocalNpcVisualAuthority() ||
            Patches.CutsceneSyncPatches.IsAuthoritativeNpcAnimationWindowActive();

        private void Update()
        {
            if (!IsSyncEnabled || !IsOnline) return;
            FlushDeferredDestroys();
        }

        private void FlushDeferredDestroys()
        {
            if (deferredDestroys.Count == 0) return;
            if (Time.realtimeSinceStartup < suppressLocalDestroyBroadcastUntil) return;

            int sent = 0;
            while (deferredDestroys.Count > 0 && sent < MaxLocalDestroyBroadcastsPerWindow)
            {
                long uniqueId = deferredDestroys[0];
                deferredDestroys.RemoveAt(0);
                // Only what is still gone here: the uid may have been reused meanwhile.
                if (WorldMap.GetWorldGameObjectByUniqueId(uniqueId, false) != null) continue;
                if (IsHost)
                    SteamP2PManager.Instance?.BroadcastWGODestroy(uniqueId, null);
                else
                    SteamP2PManager.Instance?.SendWGODestroy(uniqueId, toHost: true);
                sent++;
            }
            if (sent > 0)
            {
                CoopMod.Logger.LogInfo(
                    $"{LogPrefix} Sent {sent} held-back destroy(s); {deferredDestroys.Count} still waiting");
                // Pace the rest like any other burst.
                suppressLocalDestroyBroadcastUntil = Time.realtimeSinceStartup + LocalDestroyBurstWindowSeconds;
            }
        }

        private bool TryAllowLocalDestroyBroadcast(long uniqueId)
        {
            float now = Time.realtimeSinceStartup;
            if (now < suppressLocalDestroyBroadcastUntil) return false;

            while (recentLocalDestroyBroadcasts.Count > 0 &&
                   now - recentLocalDestroyBroadcasts.Peek() > LocalDestroyBurstWindowSeconds)
            {
                recentLocalDestroyBroadcasts.Dequeue();
            }

            recentLocalDestroyBroadcasts.Enqueue(now);
            if (recentLocalDestroyBroadcasts.Count <= MaxLocalDestroyBroadcastsPerWindow) return true;

            recentLocalDestroyBroadcasts.Clear();
            suppressLocalDestroyBroadcastUntil = now + LocalDestroyBurstSuppressSeconds;
            CoopMod.Logger.LogWarning($"{LogPrefix} Suppressing WGO destroy sync burst after {MaxLocalDestroyBroadcastsPerWindow} destroys in {LocalDestroyBurstWindowSeconds:F1}s; latest uid={uniqueId}");
            return false;
        }

        private void OnWGODestroyReceived(CSteamID senderID, long uniqueId)
        {
            if (!IsSyncEnabled) return;
            if (!IsOnline) return;

            CoopMod.Logger.LogDebug($"{LogPrefix} Received destroy for {uniqueId} from {senderID}");

            if (IsHost)
            {
                SteamP2PManager.Instance?.BroadcastWGODestroy(uniqueId, senderID);
                CoopMod.Logger.LogDebug($"{LogPrefix} Host re-broadcasting destroy: {uniqueId}");
            }

            DestroyWGOLocally(uniqueId);
        }

        private void DestroyWGOLocally(long uniqueId)
        {
            try
            {
                isProcessingRemoteDestroy = true;

                if (MainGame.me == null)
                {
                    CoopMod.Logger.LogWarning($"{LogPrefix} MainGame.me is null, cannot destroy WGO");
                    return;
                }

                var worldObjects = WGORegistry.Instance?.SnapshotAll() ?? MainGame.me?.GetListOfWorldObjects();
                if (worldObjects == null)
                {
                    CoopMod.Logger.LogWarning($"{LogPrefix} World objects list is null");
                    return;
                }

                WorldGameObject target = null;
                for (int i = 0; i < worldObjects.Count; i++)
                {
                    WorldGameObject wgo = worldObjects[i];
                    if (wgo != null && wgo.unique_id == uniqueId)
                    {
                        target = wgo;
                        break;
                    }
                }

                // DestroyMe unregisters the WGO and mutates WGORegistry's cached
                // snapshot. Resolve first and destroy after enumeration has ended.
                if (target != null)
                {
                    CoopMod.Logger.LogDebug($"{LogPrefix} Destroying WGO locally: {target.obj_id} (uid: {uniqueId})");
                    target.DestroyMe();
                }
            }
            catch (Exception ex)
            {
                CoopMod.Logger.LogWarning($"{LogPrefix} Error destroying WGO {uniqueId}: {ex.Message}");
            }
            finally
            {
                isProcessingRemoteDestroy = false;
            }
        }
    }
}
