using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Steamworks;
using UnityEngine;
using GraveyardKeeperCoop.Network;

namespace GraveyardKeeperCoop.Multiplayer
{
    public class WorkerSync : SyncBehaviour
    {
        public static WorkerSync Instance => GetInstance<WorkerSync>();

        private const byte PayloadVersion = 1;
        private const int MaxWorkers = 64;

        internal const byte SubWorkerRegistrySync = 0;
        internal const byte SubWorkerCreated = 1;
        internal const byte SubWorkerRemoved = 2;
        internal const byte SubWorkerLink = 3;

        // GKMP: which worker stands at which workbench. A world snapshot restores only the
        // linked id, so the partner never saw a zombie put on a workbench or sent back to stock.
        // Changes are collected during the frame (the game sets the link before it enables the
        // worker) and sent at its end; the host applies a client's change and passes it on.
        internal static bool IsApplyingRemoteLink;
        private readonly Dictionary<long, long> dirtyBenches = new Dictionary<long, long>();

        internal void NoteLinkChanged(WorldGameObject bench, WorldGameObject oldWorker)
        {
            if (IsApplyingRemoteLink || bench == null || bench.unique_id <= 0 || !IsSyncEnabled || !IsOnline) return;
            // Links restored from the host's world (load, snapshots) are not local changes.
            if (!MainGame.game_started || WGOStateSync.IsProcessingRemote || SpawnSync.IsApplyingCanonicalWgoState ||
                (GameLoadSync.Instance != null && GameLoadSync.Instance.IsWaitingForSync)) return;
            if (!dirtyBenches.ContainsKey(bench.unique_id))
                dirtyBenches[bench.unique_id] = oldWorker != null ? oldWorker.unique_id : -1L;
        }

        private void LateUpdate()
        {
            RetryPendingLinks();
            if (dirtyBenches.Count == 0) return;
            var batch = new List<KeyValuePair<long, long>>(dirtyBenches);
            dirtyBenches.Clear();
            if (!IsSyncEnabled || !IsOnline) return;
            foreach (var kv in batch)
            {
                var bench = WorldMap.GetWorldGameObjectByUniqueId(kv.Key, false);
                if (bench != null) SendWorkerLink(bench, kv.Value, null);
            }
        }

        private void SendWorkerLink(WorldGameObject bench, long oldWorkerUid, CSteamID? except)
        {
            var worker = bench.linked_worker;
            var old = oldWorkerUid > 0 ? WorldMap.GetWorldGameObjectByUniqueId(oldWorkerUid, false) : null;
            using (var stream = new MemoryStream(64))
            using (var bw = new BinaryWriter(stream, Encoding.UTF8))
            {
                bw.Write(PayloadVersion);
                bw.Write(SubWorkerLink);
                bw.Write(NextSequence++);
                bw.Write(bench.unique_id);
                bw.Write(worker != null ? worker.unique_id : -1L);
                bw.Write(worker != null && worker.GetParamInt("is_disabled") != 0);
                bw.Write(worker != null ? worker.transform.position.x : 0f);
                bw.Write(worker != null ? worker.transform.position.y : 0f);
                bw.Write(old != null && old != worker ? old.unique_id : -1L);
                bw.Write(old != null && old.GetParamInt("is_disabled") != 0);
                // Sent back to stock the game teleports the worker to a far point.
                bw.Write(old != null ? old.transform.position.x : 0f);
                bw.Write(old != null ? old.transform.position.y : 0f);
                bw.Flush();
                SteamP2PManager.Instance?.BroadcastWorkerSync(stream.ToArray(), except);
            }
            CoopMod.Logger.LogInfo($"{LogPrefix} Sent WorkerLink: bench={bench.obj_id}#{bench.unique_id} worker={(worker != null ? worker.unique_id : -1L)} previous={oldWorkerUid}");
        }

        private void HandleWorkerLink(BinaryReader reader, CSteamID senderID)
        {
            if (!CheckSequence(reader, senderID)) return;
            long benchUid = reader.ReadInt64();
            long workerUid = reader.ReadInt64();
            bool workerDisabled = reader.ReadBoolean();
            var workerPos = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            long oldUid = reader.ReadInt64();
            bool oldDisabled = reader.ReadBoolean();
            var oldPos = new Vector2(reader.ReadSingle(), reader.ReadSingle());

            var link = new PendingLink
            {
                BenchUid = benchUid, WorkerUid = workerUid, WorkerDisabled = workerDisabled, WorkerPos = workerPos,
                OldUid = oldUid, OldDisabled = oldDisabled, OldPos = oldPos, Sender = senderID,
                Until = Time.realtimeSinceStartup + PendingLinkSeconds
            };
            pendingLinks.Remove(benchUid);   // a newer link for the bench replaces a waiting one
            if (!TryApplyLink(link, waiting: true))
            {
                pendingLinks[benchUid] = link;
                CoopMod.Logger.LogInfo($"{LogPrefix} WorkerLink bench={benchUid} worker={workerUid} waits for the bench/worker to arrive");
            }
        }

        // GKMP: a worker made and docked in one go (a new zombie put on a porter station)
        // sends its link right behind the spawn. When the link came first the client set the
        // station to "no worker" for good; it now waits a while for both objects to arrive.
        private const float PendingLinkSeconds = 30f;
        private readonly Dictionary<long, PendingLink> pendingLinks = new Dictionary<long, PendingLink>();
        private float nextPendingLinkTry;

        private sealed class PendingLink
        {
            public long BenchUid, WorkerUid, OldUid;
            public bool WorkerDisabled, OldDisabled;
            public Vector2 WorkerPos, OldPos;
            public CSteamID Sender;
            public float Until;
        }

        private void RetryPendingLinks()
        {
            if (pendingLinks.Count == 0 || Time.realtimeSinceStartup < nextPendingLinkTry) return;
            nextPendingLinkTry = Time.realtimeSinceStartup + 0.5f;
            foreach (var link in new List<PendingLink>(pendingLinks.Values))
            {
                bool expired = Time.realtimeSinceStartup > link.Until;
                if (TryApplyLink(link, waiting: !expired) || expired)
                    pendingLinks.Remove(link.BenchUid);
            }
        }

        /// <summary>Applies a link. Returns false (nothing done) while the bench is not here,
        /// or, when <paramref name="waiting"/>, while the named worker is not here yet.</summary>
        private bool TryApplyLink(PendingLink link, bool waiting)
        {
            var bench = WorldMap.GetWorldGameObjectByUniqueId(link.BenchUid, false);
            var worker = link.WorkerUid > 0 ? WorldMap.GetWorldGameObjectByUniqueId(link.WorkerUid, false) : null;
            var old = link.OldUid > 0 ? WorldMap.GetWorldGameObjectByUniqueId(link.OldUid, false) : null;
            bool workerMissing = link.WorkerUid > 0 && (worker == null || !worker.IsWorker());
            if (bench == null)
            {
                if (!waiting)
                    CoopMod.Logger.LogWarning($"{LogPrefix} WorkerLink for unknown bench uid={link.BenchUid}");
                return false;
            }
            if (workerMissing && waiting)
                return false;
            IsApplyingRemoteLink = true;
            try
            {
                if (old != null)
                {
                    PlaceWorker(old, link.OldPos);
                    old.SetParam("is_disabled", link.OldDisabled ? 1f : 0f);
                    if (link.OldDisabled)
                    {
                        try { old.components.character.SetNoWorkerTool(); old.components.character.StopMovement(); } catch { }
                        try { old.worker?.UpdateWorkerSkin(Worker.WorkerActivity.None); } catch { }
                    }
                }
                if (worker != null && worker.IsWorker())
                {
                    worker.SetParam("is_disabled", link.WorkerDisabled ? 1f : 0f);
                    if (!worker.gameObject.activeSelf) worker.gameObject.SetActive(true);
                    PlaceWorker(worker, link.WorkerPos);
                    bench.linked_worker = worker;
                }
                else
                {
                    bench.linked_worker = null;
                }
            }
            catch (System.Exception ex)
            {
                CoopMod.Logger.LogWarning($"{LogPrefix} Failed to apply WorkerLink bench={link.BenchUid}: {ex.Message}");
            }
            finally
            {
                IsApplyingRemoteLink = false;
            }
            CoopMod.Logger.LogInfo($"{LogPrefix} Applied WorkerLink: bench={bench.obj_id}#{link.BenchUid} worker={link.WorkerUid}{(workerMissing ? " (not found)" : "")}");
            if (IsHost) SendWorkerLink(bench, link.OldUid, link.Sender);
            return true;
        }

        protected override string LogPrefix => "[WorkerSync]";

        protected override void SubscribeEvents()
        {
            if (SteamP2PManager.Instance != null)
            {
                SteamP2PManager.Instance.OnWorkerSyncReceived -= OnWorkerSyncReceived;
                SteamP2PManager.Instance.OnWorkerSyncReceived += OnWorkerSyncReceived;
            }
        }

        protected override void UnsubscribeEvents()
        {
            if (SteamP2PManager.Instance != null)
            {
                SteamP2PManager.Instance.OnWorkerSyncReceived -= OnWorkerSyncReceived;
            }
        }

        protected override void OnSyncEnabled()
        {
            SendWorkerRegistrySync();
        }

        internal void SendWorkerRegistrySync()
        {
            if (!IsSyncEnabled) return;
            if (!IsOnline) return;
            if (!IsHost) return;
            if (MainGame.me?.save?.workers == null) return;

            var workersField = typeof(SavedWorkersList).GetField("_workers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var workers = workersField?.GetValue(MainGame.me.save.workers) as List<Worker>;
            if (workers == null) return;

            int count = Mathf.Min(workers.Count, MaxWorkers);
            int estimatedSize = 256 + count * 128;
            using (var stream = new MemoryStream(estimatedSize))
            using (var bw = new BinaryWriter(stream, Encoding.UTF8))
            {
                bw.Write(PayloadVersion);
                bw.Write(SubWorkerRegistrySync);
                bw.Write(NextSequence++);

                var lastIdField = typeof(SavedWorkersList).GetField("last_unique_id",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                long lastId = lastIdField != null ? (long)lastIdField.GetValue(MainGame.me.save.workers) : 0L;
                bw.Write(lastId);
                bw.Write((ushort)count);

                for (int i = 0; i < count; i++)
                {
                    var worker = workers[i];
                    bw.Write(worker.id ?? "");
                    bw.Write(worker.worker_unique_id);
                    bool forceK = GetWorkerForceK(worker);
                    bw.Write(forceK);
                    if (forceK)
                    {
                        bw.Write(GetWorkerForcedK(worker));
                    }
                    bw.Write(GetWorkerWgoUniqueId(worker));
                }

                bw.Flush();
                SteamP2PManager.Instance?.BroadcastWorkerSync(stream.ToArray());
            }
            CoopMod.Logger.LogInfo($"{LogPrefix} Sent WorkerRegistrySync: {count} workers, last_id={0}");
        }

        internal void SendWorkerCreated(Worker worker)
        {
            if (!IsSyncEnabled) return;
            if (!IsOnline) return;
            if (!IsHost) return;

            using (var stream = new MemoryStream(128))
            using (var bw = new BinaryWriter(stream, Encoding.UTF8))
            {
                bw.Write(PayloadVersion);
                bw.Write(SubWorkerCreated);
                bw.Write(NextSequence++);
                bw.Write(worker.id ?? "");
                bw.Write(worker.worker_unique_id);
                bool forceK = GetWorkerForceK(worker);
                bw.Write(forceK);
                if (forceK)
                {
                    bw.Write(GetWorkerForcedK(worker));
                }
                bw.Write(GetWorkerWgoUniqueId(worker));
                bw.Flush();
                SteamP2PManager.Instance?.BroadcastWorkerSync(stream.ToArray());
            }
            CoopMod.Logger.LogInfo($"{LogPrefix} Sent WorkerCreated: id={worker.id}, uid={worker.worker_unique_id}");
        }

        internal void SendWorkerRemoved(long workerUniqueId)
        {
            if (!IsSyncEnabled) return;
            if (!IsOnline) return;
            if (!IsHost) return;

            using (var stream = new MemoryStream(64))
            using (var bw = new BinaryWriter(stream, Encoding.UTF8))
            {
                bw.Write(PayloadVersion);
                bw.Write(SubWorkerRemoved);
                bw.Write(NextSequence++);
                bw.Write(workerUniqueId);
                bw.Flush();
                SteamP2PManager.Instance?.BroadcastWorkerSync(stream.ToArray());
            }
            CoopMod.Logger.LogInfo($"{LogPrefix} Sent WorkerRemoved: uid={workerUniqueId}");
        }

        private void OnWorkerSyncReceived(CSteamID senderID, byte[] payload)
        {
            TryReadPayloadWithSubtype(payload, PayloadVersion, (reader, subType) =>
            {
                // Only worker links come from clients; the rest is the host's.
                if (IsHost && subType != SubWorkerLink) return;
                switch (subType)
                {
                    case SubWorkerLink:
                        HandleWorkerLink(reader, senderID);
                        break;
                    case SubWorkerRegistrySync:
                        HandleWorkerRegistrySync(reader, senderID);
                        break;
                    case SubWorkerCreated:
                        HandleWorkerCreated(reader, senderID);
                        break;
                    case SubWorkerRemoved:
                        HandleWorkerRemoved(reader, senderID);
                        break;
                    default:
                        CoopMod.Logger.LogWarning($"{LogPrefix} Unknown sub-type {subType}");
                        break;
                }
            });
        }

        private void HandleWorkerRegistrySync(BinaryReader reader, CSteamID senderID)
        {
            if (!CheckSequence(reader, senderID)) return;

            if (MainGame.me?.save?.workers == null) return;

            long lastId = reader.ReadInt64();
            int count = reader.ReadUInt16();

            var lastIdField = typeof(SavedWorkersList).GetField("last_unique_id",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (lastIdField != null)
            {
                lastIdField.SetValue(MainGame.me.save.workers, lastId);
            }

            var workersField = typeof(SavedWorkersList).GetField("_workers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var workers = workersField?.GetValue(MainGame.me.save.workers) as List<Worker>;
            if (workers == null) return;

            workers.Clear();
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                long workerUid = reader.ReadInt64();
                bool forceK = reader.ReadBoolean();
                float forcedK = 0f;
                if (forceK)
                {
                    forcedK = reader.ReadSingle();
                }
                long wgoUid = reader.ReadInt64();

                var worker = new Worker();
                worker.id = id;
                worker.worker_unique_id = workerUid;
                SetWorkerForceK(worker, forceK, forcedK);
                SetWorkerWgoUniqueId(worker, wgoUid);
                workers.Add(worker);
            }

            CoopMod.Logger.LogInfo($"{LogPrefix} Applied WorkerRegistrySync: {count} workers, last_id={lastId}");
        }

        private void HandleWorkerCreated(BinaryReader reader, CSteamID senderID)
        {
            if (!CheckSequence(reader, senderID)) return;

            if (MainGame.me?.save?.workers == null) return;

            string id = reader.ReadString();
            long workerUid = reader.ReadInt64();
            bool forceK = reader.ReadBoolean();
            float forcedK = 0f;
            if (forceK)
            {
                forcedK = reader.ReadSingle();
            }
            long wgoUid = reader.ReadInt64();

            var workersField = typeof(SavedWorkersList).GetField("_workers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var workers = workersField?.GetValue(MainGame.me.save.workers) as List<Worker>;
            if (workers == null) return;

            var worker = new Worker();
            worker.id = id;
            worker.worker_unique_id = workerUid;
            SetWorkerForceK(worker, forceK, forcedK);
            SetWorkerWgoUniqueId(worker, wgoUid);
            workers.Add(worker);

            var lastIdField = typeof(SavedWorkersList).GetField("last_unique_id",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (lastIdField != null)
            {
                long currentLastId = (long)lastIdField.GetValue(MainGame.me.save.workers);
                if (workerUid > currentLastId)
                {
                    lastIdField.SetValue(MainGame.me.save.workers, workerUid);
                }
            }

            CoopMod.Logger.LogInfo($"{LogPrefix} Applied WorkerCreated: id={id}, uid={workerUid}");
        }

        private void HandleWorkerRemoved(BinaryReader reader, CSteamID senderID)
        {
            if (!CheckSequence(reader, senderID)) return;

            if (MainGame.me?.save?.workers == null) return;

            long workerUid = reader.ReadInt64();

            var workersField = typeof(SavedWorkersList).GetField("_workers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var workers = workersField?.GetValue(MainGame.me.save.workers) as List<Worker>;
            if (workers == null) return;

            for (int i = workers.Count - 1; i >= 0; i--)
            {
                if (workers[i].worker_unique_id == workerUid)
                {
                    workers.RemoveAt(i);
                    CoopMod.Logger.LogInfo($"{LogPrefix} Removed worker uid={workerUid}");
                    break;
                }
            }
        }

        private static void PlaceWorker(WorldGameObject wgo, Vector2 pos)
        {
            if ((Vector2)wgo.transform.position == pos) return;
            var p = wgo.transform.position;
            wgo.transform.position = new Vector3(pos.x, pos.y, p.z);
            wgo.RefreshPositionCache();
        }

        private static void SetWorkerForceK(Worker worker, bool forceK, float forcedK)
        {
            if (forceK)
            {
                worker.ForcingWorkerK(true, forcedK);
            }
        }

        private static void SetWorkerWgoUniqueId(Worker worker, long wgoUid)
        {
            var field = typeof(Worker).GetField("_wgo_unique_id",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(worker, wgoUid);
            }
        }

        internal static bool GetWorkerForceK(Worker worker)
        {
            var field = typeof(Worker).GetField("_force_worker_k",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return field != null && (bool)field.GetValue(worker);
        }

        internal static float GetWorkerForcedK(Worker worker)
        {
            var field = typeof(Worker).GetField("_forced_worker_k",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return field != null ? (float)field.GetValue(worker) : 1f;
        }

        internal static long GetWorkerWgoUniqueId(Worker worker)
        {
            var field = typeof(Worker).GetField("_wgo_unique_id",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return field != null ? (long)field.GetValue(worker) : 0L;
        }
    }
}
