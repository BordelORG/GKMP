using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GraveyardKeeperCoop.Network;
using UnityEngine;

namespace CoopQA
{
    /// <summary>
    /// Sync checks inside one joined session. Each runs on both bots between Sync() barriers.
    /// Values travel over the QA side channel (not through the mod), so each side compares
    /// what the mod produced locally against what the other side reports.
    /// </summary>
    internal static class SyncTests
    {
        public static readonly List<KeyValuePair<string, Func<IEnumerator>>> All = new List<KeyValuePair<string, Func<IEnumerator>>>
        {
            new KeyValuePair<string, Func<IEnumerator>>("see_each_other", SeeEachOther),
            new KeyValuePair<string, Func<IEnumerator>>("joiner_inventory", JoinerInventory),
            new KeyValuePair<string, Func<IEnumerator>>("time", Time),
            new KeyValuePair<string, Func<IEnumerator>>("progress", Progress),
            new KeyValuePair<string, Func<IEnumerator>>("move_host", () => Move(mover: 1)),
            new KeyValuePair<string, Func<IEnumerator>>("move_client", () => Move(mover: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("chest", Chest),
            new KeyValuePair<string, Func<IEnumerator>>("chest_client_first", ChestClientFirst),
            new KeyValuePair<string, Func<IEnumerator>>("tech_host", () => Tech(buyer: 1)),
            new KeyValuePair<string, Func<IEnumerator>>("tech_client", () => Tech(buyer: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("drop_pickup", DropPickup),
            new KeyValuePair<string, Func<IEnumerator>>("craft_host", () => Craft(crafter: 1)),
            new KeyValuePair<string, Func<IEnumerator>>("craft_client", () => Craft(crafter: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("gather_host", () => Gather(gatherer: 1)),
            new KeyValuePair<string, Func<IEnumerator>>("gather_client", () => Gather(gatherer: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("combat_client", () => Combat(fighter: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("combat_host", () => Combat(fighter: 1)),
            new KeyValuePair<string, Func<IEnumerator>>("npc_talk_host", () => NpcTalk(talker: 1)),
            new KeyValuePair<string, Func<IEnumerator>>("npc_talk_client", () => NpcTalk(talker: 2)),
            // Take an organ out of the body on the table: the host the heart, the client the brain.
            new KeyValuePair<string, Func<IEnumerator>>("autopsy_host", () => Craft(1, w => w.is_autopsy_table, c => c.id.EndsWith(":heart"), StartAutopsy)),
            new KeyValuePair<string, Func<IEnumerator>>("autopsy_client", () => Craft(2, w => w.is_autopsy_table, c => c.id.EndsWith(":brain"), StartAutopsy)),
            // Replace a grave's wooden fence with a stone one (host) / another stone one (client).
            new KeyValuePair<string, Func<IEnumerator>>("grave_host", () => Craft(1, IsGrave, c => c.id == "set_grave_bot_stn_1", StartGravePart)),
            new KeyValuePair<string, Func<IEnumerator>>("grave_client", () => Craft(2, IsGrave, c => c.id == "set_grave_bot_stn_2", StartGravePart)),
            new KeyValuePair<string, Func<IEnumerator>>("trade_host", () => Trade(1)),
            new KeyValuePair<string, Func<IEnumerator>>("trade_client", () => Trade(2)),
            new KeyValuePair<string, Func<IEnumerator>>("fishing_host", () => Fishing(1)),
            new KeyValuePair<string, Func<IEnumerator>>("fishing_client", () => Fishing(2)),
            new KeyValuePair<string, Func<IEnumerator>>("build_host", () => Build(1)),
            new KeyValuePair<string, Func<IEnumerator>>("build_client", () => Build(2)),
            new KeyValuePair<string, Func<IEnumerator>>("workers", Workers),
            new KeyValuePair<string, Func<IEnumerator>>("dungeon", Dungeon),
            new KeyValuePair<string, Func<IEnumerator>>("dungeon_combat", DungeonCombat),
            new KeyValuePair<string, Func<IEnumerator>>("dungeon_levels", DungeonLevels),
            new KeyValuePair<string, Func<IEnumerator>>("sleep_rested_client", () => Sleep(clientTired: false, wakeWithKey: false)),
            new KeyValuePair<string, Func<IEnumerator>>("sleep_wake_with_key", () => Sleep(clientTired: true, wakeWithKey: true)),
            new KeyValuePair<string, Func<IEnumerator>>("time_after_sleep", Time),
            new KeyValuePair<string, Func<IEnumerator>>("progress_after_sleep", Progress),
            new KeyValuePair<string, Func<IEnumerator>>("rejoin", Rejoin),
            new KeyValuePair<string, Func<IEnumerator>>("see_each_other_after_rejoin", SeeEachOther),
            new KeyValuePair<string, Func<IEnumerator>>("move_client_after_rejoin", () => Move(mover: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("trade_client_after_rejoin", () => Trade(2)),
        };

        /// <summary>Only run when named in a suite filter (they end or need a prepared session).</summary>
        public static readonly List<KeyValuePair<string, Func<IEnumerator>>> Extra = new List<KeyValuePair<string, Func<IEnumerator>>>
        {
            new KeyValuePair<string, Func<IEnumerator>>("session_end", SessionEnd),
            new KeyValuePair<string, Func<IEnumerator>>("resume_check", ResumeCheck),
            new KeyValuePair<string, Func<IEnumerator>>("morgue_probe", MorgueProbe),
            new KeyValuePair<string, Func<IEnumerator>>("dungeon_probe", DungeonProbe),
            new KeyValuePair<string, Func<IEnumerator>>("days", Days),
            // Put first in a suite filter: the tests after it run through repeated 4 s link outages.
            new KeyValuePair<string, Func<IEnumerator>>("net_hiccups", NetHiccups),
        };

        private static int Other => Bot.IsHost ? 2 : 1;

        private static IEnumerator NetHiccups()
        {
            Bot.HiccupPeriod = 45f;
            Bot.HiccupMs = 4000;
            Bot.Step("info the link goes down for 4 s every 45 s from now on");
            yield break;
        }
        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        private static float PF(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ remote player visible where the other side really is
        private static IEnumerator SeeEachOther()
        {
            var me = MainGame.me.player;
            Bot.Publish("here", $"{F(me.pos.x)}|{F(me.pos.y)}");
            yield return Bot.WaitUntil("other side's position", () => Bot.Remote(Other, "here") != null, 30f);
            var p = Bot.Remote(Other, "here").Split('|');
            var theirs = new Vector2(PF(p[0]), PF(p[1]));
            var proxy = OnlineCoopManager.Instance.GetRemotePlayer();
            Bot.Check("remote player object exists", proxy != null);
            if (proxy == null) yield break;
            float dist = (proxy.pos - theirs).magnitude;
            var renderers = proxy.GetComponentsInChildren<SpriteRenderer>(true);
            int shown = renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy && r.sprite != null && r.color.a > 0.05f);
            Bot.Step($"info local=({F(me.pos.x)},{F(me.pos.y)}) other reports=({F(theirs.x)},{F(theirs.y)}) proxy=({F(proxy.pos.x)},{F(proxy.pos.y)}) " +
                     $"active={proxy.gameObject.activeInHierarchy} sprites shown={shown}/{renderers.Length} distance-to-me={F((proxy.pos - me.pos).magnitude)}");
            Bot.Check("remote player at the other side's real position (<24)", dist < 24f, $"off by {F(dist)}");
            // Right after joining both stand where the save put them: next to each other.
            float apart = (me.pos - theirs).magnitude;
            Bot.Check("players start next to each other (< 4 tiles)", apart > 1f && apart < 4f * 96f, $"apart={F(apart)}");
            Bot.Check("remote player active", proxy.gameObject.activeInHierarchy);
            Bot.Check("remote player has visible sprites", shown > 0, $"{shown}/{renderers.Length}");
        }

        // ------------------------------------------------------------------ day and time of day
        private static IEnumerator Time()
        {
            var save = MainGame.me.save;
            Bot.Publish("time", $"{save.day}|{F(TimeOfDay.me.time_of_day)}");
            yield return Bot.WaitUntil("other side's time", () => Bot.Remote(Other, "time") != null, 30f);
            var parts = Bot.Remote(Other, "time").Split('|');
            int otherDay = int.Parse(parts[0]);
            float otherTod = PF(parts[1]);
            float diff = Mathf.Abs(otherTod - TimeOfDay.me.time_of_day);
            Bot.Check("same day", otherDay == save.day, $"local={save.day} other={otherDay}");
            // A game day is ~22 real minutes; 0.01 of a day is ~13 s of drift.
            Bot.Check("time of day within 0.01", diff < 0.01f, $"local={F(TimeOfDay.me.time_of_day)} other={F(otherTod)} diff={F(diff)}");
        }

        // ------------------------------------------------------------------ several days in one session
        /// <summary>Static world objects (no players, NPCs, monsters or dungeon objects) as
        /// "obj_id@x,y" on a 32-unit grid; the same on both sides after any number of days.</summary>
        private static HashSet<string> WorldDigest()
        {
            var set = new HashSet<string>();
            foreach (var w in WorldMap.objs)
            {
                if (w == null || w.is_removed || w.is_player || w.obj_def == null || string.IsNullOrEmpty(w.obj_id)) continue;
                if (w.obj_def.IsNPC() || w.obj_def.type == ObjectDefinition.ObjType.Mob) continue;
                if (w.unique_id >= 2000000000000L || w.GetComponent<PlayerComponent>() != null) continue;
                set.Add($"{w.obj_id}@{Mathf.RoundToInt(w.pos.x / 32f)},{Mathf.RoundToInt(w.pos.y / 32f)}");
            }
            return set;
        }

        private static IEnumerator Days()
        {
            // One sleep until rested moves the clock ~0.23 day; sleep until three days have passed
            // and compare both sides at every new day.
            const int days = 3;
            int startDay = MainGame.me.save.day, checkedDay = startDay;
            for (int sleep = 1; sleep <= 18 && checkedDay < startDay + days; sleep++)
            {
                Bot.Scope = "days_s" + sleep;   // fresh keys for the reused sleep/time/progress checks
                Bot.Step($"info ---- sleep {sleep} (day {MainGame.me.save.day})");
                yield return Sleep(clientTired: false, wakeWithKey: false);
                yield return Bot.Sync("awake", 120f);
                // Both sides decide on the host's day, so both run the same checks.
                int day = MainGame.me.save.day;
                if (Bot.IsHost) Bot.Publish("day", day.ToString());
                else
                {
                    yield return Bot.WaitUntil("host's day", () => Bot.Remote(1, "day") != null, 30f);
                    if (Bot.Remote(1, "day") != null) day = int.Parse(Bot.Remote(1, "day"));
                }
                if (day == checkedDay) continue;
                checkedDay = day;
                Bot.Step($"info ---- day {day}: compare both sides");
                yield return Time();
                yield return Progress();
                yield return Bot.Wait(8f);   // let the world sync settle after waking
                var mine = WorldDigest();
                Bot.Publish("world", string.Join(";", mine.OrderBy(x => x, StringComparer.Ordinal)));
                yield return Bot.WaitUntil("partner's world", () => Bot.Remote(Other, "world") != null, 60f);
                var theirs = new HashSet<string>(Bot.Remote(Other, "world").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                var onlyMine = mine.Except(theirs).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var onlyTheirs = theirs.Except(mine).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Bot.Check($"day {day}: the same world objects ({mine.Count})", onlyMine.Count == 0 && onlyTheirs.Count == 0,
                          $"only here {onlyMine.Count}: [{string.Join(" ", onlyMine.Take(12))}] only P{Other} {onlyTheirs.Count}: [{string.Join(" ", onlyTheirs.Take(12))}]");
                yield return Bot.Sync("compared", 120f);
            }
            Bot.Scope = "days";
            Bot.Check($"{days} days passed in one session", checkedDay >= startDay + days, $"day {startDay} -> {checkedDay}");
        }

        // ------------------------------------------------------------------ zombie workers
        private static string WorkerInfo(WorldGameObject z) => z == null ? "none" :
            $"{z.obj_id}#{z.unique_id} worker={(z.worker == null ? "none" : z.worker.worker_unique_id.ToString())} " +
            $"bench={(z.linked_workbench == null ? "none" : z.linked_workbench.unique_id.ToString())} pos={V(z.pos)} " +
            $"disabled={z.GetParamInt("is_disabled")} active={z.gameObject.activeInHierarchy}";

        /// <summary>The host makes a zombie worker (as the game's Spawn New Worker node does), puts
        /// it on a workbench and later sends it back to stock; the partner must see each step.</summary>
        private static IEnumerator Workers()
        {
            var me = MainGame.me.player;
            if (Bot.IsHost)
            {
                // Dock points are active only near the player: go to the nearest workbenches in turn.
                var benches = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_def != null && !w.obj_def.IsPorterStation() &&
                                                       w.obj_def.has_craft && w.obj_def.interaction_type == ObjectDefinition.InteractionType.Craft &&
                                                       w.unique_id < 2000000000000L && w.linked_worker == null)
                                           .OrderBy(w => (w.pos - me.pos).sqrMagnitude).Take(6).ToList();
                WorldGameObject bench = null;
                foreach (var b in benches)
                {
                    if ((b.pos - me.pos).magnitude > 300f)
                    {
                        me.PlaceAtPos(b.pos + new Vector2(0f, -160f));
                        yield return Bot.Wait(2f);
                    }
                    if (b.GetAvailableDockPointForZombie() != null) { bench = b; break; }
                }
                Bot.Check("a workbench for a zombie", bench != null);
                if (bench == null) { Bot.Publish("zombie", "none"); yield break; }
                // The zombie worker: the definition whose wgo is not the invisible helper.
                Bot.Step("info worker definitions: " + string.Join(",", GameBalance.me.workers_data.Select(d => d.id + "->" + d.worker_wgo)));
                var def = GameBalance.me.workers_data.FirstOrDefault(d => d.id.Contains("zombie")) ??
                          GameBalance.me.workers_data.FirstOrDefault(d => d.id != "worker_invisible");
                Bot.Check("worker definition", def != null);
                if (def == null) { Bot.Publish("zombie", "none"); yield break; }
                var z = WorldMap.SpawnWGO(MainGame.me.world_root, def.worker_wgo, (Vector3)(bench.pos + new Vector2(0f, -96f)));
                var worker = MainGame.me.save.workers.CreateNewWorker(z, def.id, MainGame.me.save.GenerateBody(1, 3));
                worker.UpdateWorkerLevel();
                Bot.Step($"info made {WorkerInfo(z)} for bench {bench.obj_id}#{bench.unique_id}");
                Bot.Publish("zombie", $"{z.unique_id}|{z.obj_id}|{worker.worker_unique_id}|{bench.unique_id}");
                yield return Bot.WaitRemote(2, "zombie_seen", "1", 60f);

                var dock = bench.GetAvailableDockPointForZombie();
                z.transform.position = dock.transform.position;
                z.RefreshPositionCache();
                bench.linked_worker = z;
                z.SetParam("is_disabled", 0f);
                Bot.Step($"info on the bench: {WorkerInfo(z)}");
                Bot.Publish("zombie_on_bench", V(z.pos));
                yield return Bot.WaitRemote(2, "zombie_bench_seen", "1", 60f);

                string err = WorldMap.RemoveZombieWorkerToStock(z);
                Bot.Check("worker sent to stock", string.IsNullOrEmpty(err), err);
                Bot.Step($"info in stock: {WorkerInfo(z)}");
                Bot.Publish("zombie_stock", V(z.pos));
                yield return Bot.WaitRemote(2, "zombie_stock_seen", "1", 60f);
            }
            else
            {
                yield return Bot.WaitUntil("host made a zombie", () => Bot.Remote(1, "zombie") != null, 60f);
                var p = Bot.Remote(1, "zombie").Split('|');
                if (p[0] == "none") yield break;
                long zid = long.Parse(p[0]), wid = long.Parse(p[2]), bid = long.Parse(p[3]);
                WorldGameObject Z() => WorldMap.GetWorldGameObjectByUniqueId(zid, false);
                yield return Bot.WaitUntil("the zombie is here", () => Z() != null && !Z().is_removed, 30f);
                Bot.Step("info here: " + WorkerInfo(Z()));
                Bot.Check("same kind of zombie", Z() != null && Z().obj_id == p[1], WorkerInfo(Z()));
                yield return Bot.WaitUntil("it is a worker here", () => Z()?.worker != null && Z().worker.worker_unique_id == wid, 20f);
                Bot.Publish("zombie_seen", "1");

                yield return Bot.WaitUntil("host put it on the bench", () => Bot.Remote(1, "zombie_on_bench") != null, 60f);
                var bench = WorldMap.GetWorldGameObjectByUniqueId(bid, false);
                Bot.Check("the bench is here", bench != null);
                yield return Bot.WaitUntil("the bench has the zombie here", () => bench != null && bench.linked_worker != null && bench.linked_worker.unique_id == zid, 20f);
                yield return Bot.WaitUntil("the zombie stands at the bench", () => Z() != null && Vector2.Distance(Z().pos, ParseV(Bot.Remote(1, "zombie_on_bench"))) < 40f, 20f);
                Bot.Step("info on the bench here: " + WorkerInfo(Z()));
                Bot.Publish("zombie_bench_seen", "1");

                yield return Bot.WaitUntil("host sent it to stock", () => Bot.Remote(1, "zombie_stock") != null, 60f);
                yield return Bot.WaitUntil("the bench is free here", () => bench == null || bench.linked_worker == null, 20f);
                yield return Bot.WaitUntil("the zombie went to stock here", () => Z() == null || Vector2.Distance(Z().pos, ParseV(Bot.Remote(1, "zombie_stock"))) < 40f, 20f);
                Bot.Check("the zombie is disabled here", Z() == null || Z().GetParamInt("is_disabled") == 1, WorkerInfo(Z()));
                Bot.Step("info in stock here: " + WorkerInfo(Z()));
                Bot.Publish("zombie_stock_seen", "1");
            }
        }

        // ------------------------------------------------------------------ quests, NPC tasks, technologies
        private static Dictionary<string, string> ProgressParts()
        {
            var save = MainGame.me.save;
            var qs = save.quests;
            var current = qs.GetCurrentQuests().Select(q => q.definition.id + ":" + q.state).OrderBy(x => x, StringComparer.Ordinal);
            var succeeded = Bot.Get<List<string>>(qs, "_succed_quests").OrderBy(x => x, StringComparer.Ordinal);
            var failed = Bot.Get<List<string>>(qs, "_failed_quests").OrderBy(x => x, StringComparer.Ordinal);
            var tasks = save.known_npcs.npcs.SelectMany(n => n.tasks.Select(t => n.npc_id + "/" + t.id + ":" + t.state)).OrderBy(x => x, StringComparer.Ordinal);
            var techs = save.unlocked_techs.OrderBy(x => x, StringComparer.Ordinal);
            return new Dictionary<string, string>
            {
                ["quests"] = string.Join(",", current),
                ["succeeded"] = string.Join(",", succeeded),
                ["failed"] = string.Join(",", failed),
                ["npc_tasks"] = string.Join(",", tasks),
                ["techs"] = string.Join(",", techs),
            };
        }

        private static IEnumerator Progress()
        {
            // QuestSync and TechSync run every 10 s; give one cycle after joining.
            yield return Bot.Wait(12f);
            var mine = ProgressParts();
            foreach (var kv in mine) Bot.Publish("prog." + kv.Key, kv.Value);
            foreach (var k in mine.Keys)
                yield return Bot.WaitUntil("other side's " + k, () => Bot.Remote(Other, "prog." + k) != null, 30f);
            foreach (var kv in mine)
            {
                var theirs = Bot.Remote(Other, "prog." + kv.Key);
                var a = new HashSet<string>(kv.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                var b = new HashSet<string>(theirs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                var onlyMine = a.Except(b).Take(8).ToList();
                var onlyTheirs = b.Except(a).Take(8).ToList();
                Bot.Check($"{kv.Key} equal ({a.Count} entries)", onlyMine.Count == 0 && onlyTheirs.Count == 0,
                    $"only here: [{string.Join(" ", onlyMine)}] only P{Other}: [{string.Join(" ", onlyTheirs)}]");
            }
        }

        // ------------------------------------------------------------------ movement seen by the other side
        private static IEnumerator Move(int mover)
        {
            if (Plugin.Player == mover)
            {
                var player = MainGame.me.player;
                Vector2 start = player.pos;
                bool done = false;
                // Walk like the keyboard does (BaseCharacterComponent.OnChangeDir); try another
                // direction if something blocks the way.
                var onChangeDir = HarmonyLib.AccessTools.Method(typeof(BaseCharacterComponent), "OnChangeDir");
                foreach (var dir in new[] { Vector2.right, Vector2.left, Vector2.down, Vector2.up })
                {
                    float t0 = UnityEngine.Time.realtimeSinceStartup;
                    while (UnityEngine.Time.realtimeSinceStartup - t0 < 1.5f)
                    {
                        onChangeDir.Invoke(MainGame.me.player_char, new object[] { dir });
                        yield return null;
                    }
                    onChangeDir.Invoke(MainGame.me.player_char, new object[] { Vector2.zero });
                    yield return Bot.Wait(0.3f);
                    if ((player.pos - start).magnitude > 40f) { done = true; break; }
                }
                yield return Bot.Wait(0.5f);
                Vector2 end = player.pos;
                Bot.Step($"info moved {F((end - start).magnitude)} units (arrived={done}) to {F(end.x)},{F(end.y)}");
                Bot.Publish("pos", $"{F(end.x)}|{F(end.y)}|{F((end - start).magnitude)}");
                yield return Bot.WaitRemote(Other, "seen", "1", 30f);
            }
            else
            {
                yield return Bot.WaitUntil("mover reported position", () => Bot.Remote(mover, "pos") != null, 30f);
                var p = Bot.Remote(mover, "pos").Split('|');
                var target = new Vector2(PF(p[0]), PF(p[1]));
                float moved = PF(p[2]);
                Bot.Check("mover actually moved", moved > 20f, $"moved={F(moved)}");
                var ocm = OnlineCoopManager.Instance;
                float dist = float.MaxValue;
                yield return Bot.WaitUntil("remote player shown at reported position (<24 units)", () =>
                {
                    var remote = ocm.GetRemotePlayer();
                    if (remote == null) return false;
                    dist = (remote.pos - target).magnitude;
                    return dist < 24f;
                }, 6f);
                Bot.Step($"info remote proxy distance {F(dist)}");
                Bot.Publish("seen", "1");
            }
            yield return Bot.Sync("move_done_" + mover, 30f);
            Bot.Publish("pos", null);
            Bot.Publish("seen", null);
        }

        // ------------------------------------------------------------------ shared chest through the real ChestGUI path
        private static WorldGameObject FindChest()
        {
            var me = MainGame.me.player.pos;
            return WorldMap.objs
                .Where(w => w != null && !w.is_player && w.unique_id > 0 && w.data?.inventory != null &&
                            w.obj_id != null && w.obj_id.Contains("chest"))
                .OrderBy(w => (w.pos - me).sqrMagnitude)
                .FirstOrDefault();
        }

        private static IEnumerator MoveViaChestGui(WorldGameObject chest, string itemId, bool toChest)
        {
            var gui = GUIElements.me.chest;
            gui.Open(chest);
            yield return Bot.Wait(0.5f);
            var source = toChest ? MainGame.me.player.data : chest.data;
            var item = source.inventory.FirstOrDefault(i => i != null && i.id == itemId);
            Bot.Check($"item {itemId} present on the {(toChest ? "player" : "chest")} side", item != null);
            if (item == null) { gui.Hide(); yield break; }
            var panel = toChest ? gui.player_panel : gui.chest_panel;
            HarmonyLib.AccessTools.Field(typeof(InventoryPanelGUI), "_selected_item").SetValue(panel, item);
            HarmonyLib.AccessTools.Method(typeof(ChestGUI), "MoveItem",
                    new[] { typeof(Item), typeof(int), typeof(bool), typeof(Item), typeof(bool) })
                .Invoke(gui, new object[] { item, 1, toChest, null, false });
            yield return Bot.Wait(1.5f);
            gui.Hide();
            yield return Bot.Wait(0.5f);
        }

        private static IEnumerator Chest()
        {
            if (Bot.IsHost)
            {
                var chest = FindChest();
                Bot.Check("host found a chest", chest != null);
                if (chest == null) { Bot.Publish("chest", "none"); yield break; }
                var item = MainGame.me.player.data.inventory.FirstOrDefault(i => i != null && !i.IsEmpty() && !i.is_bag && i.value > 1);
                Bot.Check("host has a stackable item to share", item != null);
                if (item == null) { Bot.Publish("chest", "none"); yield break; }
                string id = item.id;
                int before = chest.data.GetItemsCount(id);
                yield return MoveViaChestGui(chest, id, toChest: true);
                int after = chest.data.GetItemsCount(id);
                Bot.Check("host put 1 item in the chest", after == before + 1, $"{id}: {before} -> {after}");
                Bot.Publish("chest", $"{chest.unique_id}|{id}|{after}");
                yield return Bot.WaitRemote(2, "chest_took", "1", 60f);
                yield return Bot.WaitUntil($"host sees client's withdrawal ({id} back to {before})", () => chest.data.GetItemsCount(id) == before, 15f);
            }
            else
            {
                yield return Bot.WaitUntil("host shared chest info", () => Bot.Remote(1, "chest") != null, 60f);
                if (Bot.Remote(1, "chest") == "none") yield break;
                var p = Bot.Remote(1, "chest").Split('|');
                long uid = long.Parse(p[0]);
                string id = p[1];
                int expected = int.Parse(p[2]);
                var chest = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
                Bot.Check("client has the same chest", chest != null, $"uid={uid}");
                if (chest == null) yield break;
                yield return Bot.WaitUntil($"client sees host's deposit ({id}={expected})", () => chest.data.GetItemsCount(id) == expected, 15f);
                int mineBefore = MainGame.me.player.data.GetItemsCount(id);
                yield return MoveViaChestGui(chest, id, toChest: false);
                yield return Bot.WaitUntil("item arrived in client inventory", () => MainGame.me.player.data.GetItemsCount(id) == mineBefore + 1, 15f);
                yield return Bot.WaitUntil("chest count dropped on client", () => chest.data.GetItemsCount(id) == expected - 1, 15f);
                Bot.Publish("chest_took", "1");
            }
        }

        // ------------------------------------------------------------------ client deposits first, host sees it
        private static IEnumerator ChestClientFirst()
        {
            if (!Bot.IsHost)
            {
                var hostChest = Bot.RemoteIn(1, "chest", "chest");
                var chest = hostChest != null && hostChest != "none"
                    ? WorldMap.GetWorldGameObjectByUniqueId(long.Parse(hostChest.Split('|')[0]), false) : FindChest();
                Bot.Check("client found the shared chest", chest != null);
                string want = hostChest != null && hostChest != "none" ? hostChest.Split('|')[1] : null;
                var item = MainGame.me.player.data.inventory.FirstOrDefault(i => i != null && !i.IsEmpty() && i.id == want)
                        ?? MainGame.me.player.data.inventory.FirstOrDefault(i => i != null && !i.IsEmpty() && !i.is_bag && i.value > 1);
                Bot.Check("client has an item to share", item != null);
                if (chest == null || item == null) { Bot.Publish("cchest", "none"); yield break; }
                // Stand next to the chest like a player who walked there.
                MainGame.me.player.PlaceAtPos(chest.pos + new Vector2(0f, -80f));
                yield return Bot.Wait(1f);
                string id = item.id;
                int before = chest.data.GetItemsCount(id);
                yield return MoveViaChestGui(chest, id, toChest: true);
                int after = chest.data.GetItemsCount(id);
                Bot.Check("client put 1 item in the chest (local view)", after == before + 1, $"{id}: {before} -> {after}");
                Bot.Publish("cchest", $"{chest.unique_id}|{id}|{before + 1}");
                yield return Bot.WaitRemote(1, "cchest_seen", "1", 30f);
                Bot.Check("client chest count stable after host ack", chest.data.GetItemsCount(id) == before + 1, $"now {chest.data.GetItemsCount(id)}");
            }
            else
            {
                yield return Bot.WaitUntil("client shared chest info", () => Bot.Remote(2, "cchest") != null, 60f);
                if (Bot.Remote(2, "cchest") == "none") { Bot.Publish("cchest_seen", "1"); yield break; }
                var p = Bot.Remote(2, "cchest").Split('|');
                var chest = WorldMap.GetWorldGameObjectByUniqueId(long.Parse(p[0]), false);
                Bot.Check("host has the same chest", chest != null);
                if (chest != null)
                    yield return Bot.WaitUntil($"host sees client's deposit ({p[1]}={p[2]})", () => chest.data.GetItemsCount(p[1]) == int.Parse(p[2]), 15f);
                Bot.Publish("cchest_seen", "1");
            }
        }

        // ------------------------------------------------------------------ buying a technology in the tech tree
        private static IEnumerator Tech(int buyer)
        {
            var save = MainGame.me.save;
            if (Plugin.Player == buyer)
            {
                var all = GameBalance.me.techs_data;
                var affordable = all.Where(t => save.CanBuyTech(t.id)).Select(t => t.id).ToList();
                string pts = $"r={F(MainGame.me.player.GetParam("r"))} g={F(MainGame.me.player.GetParam("g"))} b={F(MainGame.me.player.GetParam("b"))}";
                Bot.Step($"info affordable techs: {affordable.Count} ({string.Join(",", affordable.Take(6))}) points {pts}");
                string tech = affordable.FirstOrDefault(t => Bot.Remote(Other, "tech_bought") != t);
                bool bought;
                if (tech != null)
                {
                    bought = save.BuyTech(tech);   // what TechUnlockDialogGUI does on confirm
                    Bot.Check("BuyTech succeeded", bought, tech);
                }
                else
                {
                    // No points in the fixture: unlock a locked tech whose parents are unlocked,
                    // the same call BuyTech makes after paying.
                    tech = all.Where(t => !save.unlocked_techs.Contains(t.id) && t.parents.All(par => save.unlocked_techs.Contains(par.id)))
                              .Select(t => t.id).FirstOrDefault(t => Bot.Remote(Other, "tech_bought") != t);
                    Bot.Step($"info no affordable tech, unlocking '{tech}' directly");
                    if (tech != null) save.UnlockTech(tech);
                }
                Bot.Publish("tech_bought", tech ?? "none");
                // Watch the buyer's own copy for a few seconds: it must never be un-learned,
                // not even briefly by a host snapshot that predates the purchase.
                bool lost = false;
                float w0 = UnityEngine.Time.realtimeSinceStartup;
                while (UnityEngine.Time.realtimeSinceStartup - w0 < 3f || Bot.Remote(Other, "tech_seen") == null)
                {
                    if (tech != null && !save.unlocked_techs.Contains(tech)) lost = true;
                    if (UnityEngine.Time.realtimeSinceStartup - w0 > 40f) break;
                    yield return null;
                }
                Bot.Check("other side saw the tech", Bot.Remote(Other, "tech_seen") == "1");
                Bot.Check("tech never lost on the buyer", !lost && (tech == null || save.unlocked_techs.Contains(tech)));
            }
            else
            {
                yield return Bot.WaitUntil("buyer picked a tech", () => Bot.Remote(buyer, "tech_bought") != null, 30f);
                string tech = Bot.Remote(buyer, "tech_bought");
                if (tech != "none")
                    yield return Bot.WaitUntil($"tech '{tech}' unlocked here too", () => save.unlocked_techs.Contains(tech), 25f);
                Bot.Publish("tech_seen", "1");
            }
            yield return Bot.Sync("tech_done_" + buyer, 30f);
            Bot.Publish("tech_seen", null);
        }

        // ------------------------------------------------------------------ item dropped on the ground, picked up by the other player
        private static List<DropResGameObject> Drops(string id) =>
            UnityEngine.Object.FindObjectsOfType<DropResGameObject>().Where(d => d != null && !d.is_collected && d.res != null && d.res.id == id).ToList();

        private static IEnumerator DropPickup()
        {
            if (!Bot.IsHost)
            {
                var item = MainGame.me.player.data.inventory.FirstOrDefault(i => i != null && !i.IsEmpty() && !i.is_bag && i.value >= 1);
                if (item == null)
                {
                    // A fresh joiner has an empty bag: pick up some wheat first.
                    MainGame.me.player.data.AddItem("wheat_crop", 2);
                    item = MainGame.me.player.data.inventory.FirstOrDefault(i => i != null && i.id == "wheat_crop");
                }
                Bot.Check("client has an item to drop", item != null);
                if (item == null) { Bot.Publish("drop", "none"); yield break; }
                string id = item.id;
                int before = MainGame.me.player.data.GetItemsCount(id);
                int dropsBefore = Drops(id).Count;
                MainGame.me.player.data.RemoveItem(id, 1);
                // Like a harvest/kill drop: lands a few tiles away from the player.
                // Away from the host: a player standing on a drop picks it up at once.
                var hostProxy = OnlineCoopManager.Instance?.GetRemotePlayer();
                float side = hostProxy != null && hostProxy.transform.position.x > MainGame.me.player.transform.position.x ? -1f : 1f;
                Vector3 spot = MainGame.me.player.transform.position + new Vector3(250f * side, 0f, 0f);
                DropResGameObject.Drop(spot, new Item(id, 1), MainGame.me.player.transform.parent);
                yield return Bot.Wait(2f);
                Bot.Check("client sees its own drop", Drops(id).Count > dropsBefore, $"{Drops(id).Count} drop(s) of {id}");
                var d = Drops(id).OrderBy(x => (x.transform.position - MainGame.me.player.transform.position).sqrMagnitude).FirstOrDefault();
                Vector3 pos = d != null ? d.transform.position : MainGame.me.player.transform.position;
                Bot.Publish("drop", $"{id}|{F(pos.x)}|{F(pos.y)}|{before - 1}");
                yield return Bot.WaitRemote(1, "drop_done", "1", 60f);
                yield return Bot.WaitUntil("drop gone on client after host picked it up", () => Drops(id).Count <= dropsBefore, 15f);
            }
            else
            {
                yield return Bot.WaitUntil("client dropped something", () => Bot.Remote(2, "drop") != null, 60f);
                if (Bot.Remote(2, "drop") == "none") yield break;
                var p = Bot.Remote(2, "drop").Split('|');
                string id = p[0];
                var at = new Vector3(PF(p[1]), PF(p[2]), 0f);
                DropResGameObject drop = null;
                yield return Bot.WaitUntil($"host sees the client's {id} on the ground", () =>
                {
                    drop = Drops(id).OrderBy(x => Vector2.Distance(x.transform.position, at)).FirstOrDefault();
                    return drop != null && Vector2.Distance(drop.transform.position, at) < 200f;
                }, 15f);
                if (drop != null)
                {
                    int mine = MainGame.me.player.data.GetItemsCount(id);
                    Vector3 back = MainGame.me.player.transform.position;
                    MainGame.me.player.PlaceAtPos(drop.transform.position);   // walk onto it
                    float t0 = UnityEngine.Time.realtimeSinceStartup;
                    while (UnityEngine.Time.realtimeSinceStartup - t0 < 4f && !drop.is_collected) yield return null;
                    if (!drop.is_collected)
                    {
                        Bot.Step("info drop not auto-collected on walk-over, using CollectDrop");
                        drop.CollectDrop(MainGame.me.player);
                    }
                    yield return Bot.WaitUntil("item in host inventory", () => MainGame.me.player.data.GetItemsCount(id) == mine + 1, 8f);
                    MainGame.me.player.PlaceAtPos(back);
                }
                Bot.Publish("drop_done", "1");
            }
        }

        // ------------------------------------------------------------------ both players go to bed, the night passes for both
        private static WorldGameObject FindBed()
        {
            var me = MainGame.me.player.pos;
            var beds = WorldMap.objs.Where(w => w != null && w.obj_id != null && w.obj_id.ToLowerInvariant().Contains("bed") && !w.is_player).ToList();
            Bot.Step("info bed candidates: " + string.Join(",", beds.Select(b => $"{b.obj_id}#{b.unique_id}@{F((b.pos - me).magnitude)}").Take(10)));
            // The house bed is obj_id "bed"; corpse_bed / bed_no_sleep_* are not for sleeping.
            return beds.FirstOrDefault(b => b.obj_id == "bed");
        }

        private static double Clock() => MainGame.me.save.day + TimeOfDay.me.GetTimeK();

        /// <summary>
        /// Host is tired and lies down first: it must wait for the client (night does not pass).
        /// Then the client lies down (rested or tired). Either everyone sleeps until rested, or
        /// the client presses Esc/E mid-night, which must wake both.
        /// </summary>
        private static IEnumerator Sleep(bool clientTired, bool wakeWithKey)
        {
            var save = MainGame.me.save;
            var me = MainGame.me.player;
            var sleepGui = GUIElements.me.sleep_gui;
            var gts = GraveyardKeeperCoop.Multiplayer.GameTimeSync.Instance;
            var bed = FindBed();
            Bot.Check("bed found", bed != null);
            if (bed == null || gts == null) yield break;

            // A day of work: the host is tired; the client is tired only when asked.
            if (Bot.IsHost || clientTired) me.energy = Mathf.Max(1f, save.max_energy * 0.3f);
            else { me.energy = save.max_energy; me.sanity = save.max_sanity; me.hp = save.max_hp; }
            double r0 = Clock();
            yield return Bot.Wait(4f);
            double awakeRate = (Clock() - r0) / 4.0;
            double startClock = Clock();
            Bot.Step($"info clock={startClock:F3} energy={F(me.energy)}/{F(save.max_energy)} sanity={F(me.sanity)}");

            if (!Bot.IsHost) yield return Bot.WaitRemote(1, "in_bed", "1", 90f);
            me.PlaceAtPos(bed.pos + new Vector2(0f, -60f));
            yield return Bot.Wait(0.5f);
            bed.Interact(me, interaction_start: true);   // pressing E at the bed
            yield return Bot.WaitUntil("lying in bed (sleep started)", () => gts.IsLocalSleeping, 20f);

            if (Bot.IsHost)
            {
                double c0 = Clock();
                yield return Bot.Wait(4f);
                double waited = Clock() - c0;
                Bot.Step($"info alone in bed: {waited / 4.0:F5} day/s (awake rate {awakeRate:F5})");
                Bot.Check("alone in bed: host waits for the partner", gts.IsLocalSleepWaitingForParty, $"waiting={gts.IsLocalSleepWaitingForParty}");
                // 4 real seconds at normal speed is ~0.003 of a day; fast-forward would be ~10x.
                Bot.Check("alone in bed: the night does not fast-forward", waited / 4.0 < awakeRate * 2.0, $"advanced {waited:F4} day in 4 s");
                Bot.Publish("in_bed", "1");
            }

            if (wakeWithKey)
            {
                yield return Bot.WaitUntil("whole party asleep (fast-forward)", () => !gts.IsLocalSleepWaitingForParty, 20f);
                if (!Bot.IsHost)
                {
                    yield return Bot.Wait(3f);
                    Bot.Step("info client presses Esc to get up mid-night");
                    HarmonyLib.AccessTools.Method(typeof(SleepGUI), "OnPressedBack").Invoke(sleepGui, null);
                }
                yield return Bot.WaitUntil("everyone woke up after the key press", () => !gts.IsLocalSleeping && !sleepGui.is_shown, 20f);
                Bot.Check("woken before fully rested", me.energy < save.max_energy, $"energy={F(me.energy)}");
            }
            else
            {
                yield return Bot.WaitUntil("slept until rested and woke up", () => !gts.IsLocalSleeping && !sleepGui.is_shown, 150f);
                Bot.Check("energy restored", me.energy >= save.max_energy - 1f, $"energy={F(me.energy)}");
            }
            yield return Bot.WaitUntil("player has control again", () => MainGame.me.player_char.control_enabled, 60f);
            double advanced = Clock() - startClock;
            Bot.Step($"info after sleep clock={Clock():F3} advanced={advanced:F3} day(s) energy={F(me.energy)}");
            Bot.Check("time moved forward while sleeping", advanced > 0.02, $"{advanced:F3}");
            yield return Bot.Wait(3f);
        }

        // ------------------------------------------------------------------ crafting at a workstation (pick recipe, hold E)
        private static bool HasNeeds(CraftDefinition c) =>
            c.needs != null && c.needs.All(n => n != null && MainGame.me.player.data.GetTotalCount(n.id, true) >= Math.Max(1, n.value));

        /// <summary>Contents of a station including what is inside its items (a body's organs).</summary>
        private static string StationState(WorldGameObject w) =>
            w?.data == null ? "none" :
            ParamsOf(w.data) + " " +
            string.Join(";", w.data.inventory.Where(i => i != null && !i.IsEmpty())
                              .Select(i => i.id + "x" + i.value + ParamsOf(i) + (i.inventory != null && i.inventory.Count > 0 ? "[" + Items(i.inventory) + "]" : ""))
                              .OrderBy(x => x));

        private static readonly System.Reflection.FieldInfo ParamsF = HarmonyLib.AccessTools.Field(typeof(Item), "_params");
        private static readonly System.Reflection.FieldInfo ResTypeF = HarmonyLib.AccessTools.Field(typeof(GameRes), "_res_type");

        /// <summary>An item's params (quality, skulls, ...) sorted by key, as "{k=v,...}".</summary>
        private static string ParamsOf(Item i)
        {
            var res = ParamsF.GetValue(i) as GameRes;
            var keys = res == null ? null : ResTypeF.GetValue(res) as List<string>;
            if (keys == null || keys.Count == 0) return "";
            // A zero param equals a missing one (the game prunes zeros on some writes).
            var set = keys.Distinct().Where(k => Mathf.Abs(res.Get(k)) > 0.0001f).OrderBy(k => k).ToList();
            return set.Count == 0 ? "" : "{" + string.Join(",", set.Select(k => k + "=" + F(res.Get(k)))) + "}";
        }

        private static IEnumerator Craft(int crafter) => Craft(crafter, null, null);

        private static IEnumerator Craft(int crafter, Func<WorldGameObject, bool> stationFilter, Func<CraftDefinition, bool> craftFilter) =>
            Craft(crafter, stationFilter, craftFilter, null);

        /// <summary>What AutopsyGUI does on a click: take the organ out of the body, then craft with it.</summary>
        private static bool StartAutopsy(WorldGameObject table, CraftDefinition craft)
        {
            string organ = craft.id.Substring(craft.id.LastIndexOf(':') + 1);
            var body = table.data.inventory.FirstOrDefault(i => i != null && i.id == "body");
            Item part = null;
            if (body != null)
                foreach (var i in body.inventory)
                {
                    if (i == null) continue;
                    if (i.id == organ || i.id.StartsWith(organ + ":")) { part = i; break; }
                }
            if (part == null) { Bot.Step($"info no {organ} in the body"); return false; }
            HarmonyLib.AccessTools.Method(typeof(AutopsyGUI), "RemoveBodyPartFromBody").Invoke(null, new object[] { body, part });
            return table.components.craft.CraftAsPlayer(craft, part);
        }

        /// <summary>What GraveGUI does after picking a part: craft with that item from the bag.</summary>
        private static bool StartGravePart(WorldGameObject grave, CraftDefinition craft)
        {
            var me = MainGame.me.player;
            var part = me.data.inventory.FirstOrDefault(i => i != null && craft.needs.Any(n => n.id == i.id));
            return grave.components.craft.CraftAsPlayer(craft, part);
        }

        private static bool IsGrave(WorldGameObject w) =>
            w.obj_def?.interaction_type == ObjectDefinition.InteractionType.Grave &&
            w.data.inventory.Any(i => i != null && i.id != null && i.id.StartsWith("grave_bot_wd"));

        private static IEnumerator Craft(int crafter, Func<WorldGameObject, bool> stationFilter, Func<CraftDefinition, bool> craftFilter,
                                         Func<WorldGameObject, CraftDefinition, bool> start)
        {
            if (Plugin.Player == crafter)
            {
                var me = MainGame.me.player;
                var candidates = WorldMap.objs
                    .Where(w => w != null && w.components?.craft != null && w.unique_id > 0 && !w.is_player &&
                                !w.components.craft.is_crafting && w.components.craft.crafts != null &&
                                (stationFilter == null || stationFilter(w)))
                    .OrderBy(w => (w.pos - me.pos).sqrMagnitude).Take(40).ToList();
                Func<CraftDefinition, bool> wanted = c => craftFilter != null ? craftFilter(c) : c.needs.Count > 0 && !c.id.Contains("remove");
                WorldGameObject station = null; CraftDefinition craft = null;
                foreach (var w in candidates)
                {
                    craft = w.components.craft.crafts.FirstOrDefault(c => c != null && !c.is_auto && (c.output.Count > 0 || craftFilter != null) &&
                                                                         wanted(c) && HasNeeds(c));
                    if (craft != null) { station = w; break; }
                }
                if (station == null)
                {
                    // Nothing affordable: gather the materials for the simplest recipe nearby
                    // (stands in for collecting them in the world).
                    foreach (var w in candidates.Where(w => w.components.craft.crafts.Count > 0))
                    {
                        craft = w.components.craft.crafts
                            .Where(c => c != null && !c.is_auto && (c.output.Count > 0 || craftFilter != null) && wanted(c) &&
                                        c.needs.All(n => n != null && GameBalance.me.GetDataOrNull<ItemDefinition>(n.id) != null))
                            .OrderBy(c => c.needs.Count).FirstOrDefault();
                        if (craft == null) continue;
                        station = w;
                        Bot.Step($"info inventory: size={me.data.inventory_size} used={me.data.inventory.Count(i => i != null && !i.IsEmpty())} " +
                                 string.Join(",", me.data.inventory.Where(i => i != null && !i.IsEmpty()).Select(i => i.id + "x" + i.value)));
                        foreach (var n in craft.needs)
                            me.data.AddItem(n.id, Math.Max(1, n.value));
                        Bot.Step("info after gathering: " + string.Join(",", craft.needs.Select(n => $"{n.id} {me.data.GetTotalCount(n.id, true)}")));
                        Bot.Step("info gathered materials: " + string.Join(",", craft.needs.Select(n => n.id + "x" + n.value)));
                        break;
                    }
                }
                Bot.Step("info stations nearby: " + string.Join(",", candidates.Take(8).Select(w => $"{w.obj_id}({w.components.craft.crafts.Count})")));
                Bot.Check("found a station with an affordable recipe", station != null);
                if (station == null) { Bot.Publish("craft", "none"); yield break; }
                var cc = station.components.craft;
                string needs = string.Join(",", craft.needs.Select(n => n.id + "x" + n.value));
                string outputs = string.Join(",", craft.output.Select(o => o.id + "x" + o.value));
                Bot.Step($"info crafting '{craft.id}' at {station.obj_id}#{station.unique_id}: needs {needs} -> {outputs}");
                var outIds = craft.output.Select(o => o.id).Where(id => id.Length > 1).ToList();
                // Organs come out as "heart:heart_2_0" (the kind of heart), so count by prefix.
                Func<int> haveOut = () => outIds.Sum(id => me.data.GetTotalCount(id, true) +
                    me.data.inventory.Where(i => i != null && i.id != null && i.id.StartsWith(id + ":")).Sum(i => i.value));
                int outBefore = haveOut();
                Func<int> dropsAtStation = () => outIds.Sum(id => Drops(id).Count(d => Vector2.Distance(d.transform.position, station.transform.position) < 400f));
                int dropsBefore = dropsAtStation();
                string stationBefore = StationState(station);
                // Walk up to the station from each side (keyboard walking) until the game
                // highlights it as the object in front of the player.
                var inter = me.components.interaction;
                var onChangeDir = HarmonyLib.AccessTools.Method(typeof(BaseCharacterComponent), "OnChangeDir");
                foreach (var dir in new[] { Vector2.up, Vector2.right, Vector2.left, Vector2.down })
                {
                    me.PlaceAtPos(station.pos - dir * 200f);
                    yield return Bot.Wait(0.4f);
                    float w0 = UnityEngine.Time.realtimeSinceStartup;
                    while (inter.nearest != station && UnityEngine.Time.realtimeSinceStartup - w0 < 2.5f)
                    {
                        onChangeDir.Invoke(MainGame.me.player_char, new object[] { dir });
                        yield return null;
                    }
                    onChangeDir.Invoke(MainGame.me.player_char, new object[] { Vector2.zero });
                    yield return Bot.Wait(0.4f);
                    if (inter.nearest == station) break;
                }
                // Stations worked from a dock point (graves): stand on the closest one, as the
                // player's walk to it would end there (the start spot may have no path).
                var docks = HarmonyLib.AccessTools.Field(typeof(WorldGameObject), "_dock_points").GetValue(station) as DockPoint[];
                if (docks != null && docks.Length > 0 && station.obj_def?.interaction_type == ObjectDefinition.InteractionType.Grave)
                {
                    foreach (var dp in docks.Where(x => x != null).OrderBy(x => ((Vector2)x.transform.position - me.pos).sqrMagnitude))
                    {
                        me.PlaceAtPos(dp.transform.position);
                        yield return Bot.Wait(0.6f);
                        Bot.Step($"info stood on a dock point ({docks.Length} total); nearest={inter.nearest?.obj_id}");
                        if (inter.nearest == station) break;
                    }
                }
                Bot.Step($"info nearest after walking up: {inter.nearest?.obj_id}#{inter.nearest?.unique_id} tool={me.GetEquippedTool()?.id}");
                Bot.Step("info before start: " + string.Join(",", craft.needs.Select(n => $"{n.id} {me.data.GetTotalCount(n.id, true)}/{n.value}")) +
                         $" craft tools=[{string.Join(",", craft.tool_actions.action_tools)}] station tools=[{string.Join(",", station.obj_def.tool_actions.action_tools)}]" +
                         $" belt=[{string.Join(",", me.data.secondary_inventory.Where(i => i != null && !i.IsEmpty()).Select(i => i.id + ":" + i.definition?.type))}]");
                // The previous user's work must be over: a station still "working" makes the game
                // continue that craft instead of starting the one the player chose.
                yield return Bot.WaitUntil("station idle before I start", () => !cc.is_crafting, 10f);
                bool started = start != null ? start(station, craft) : cc.CraftAsPlayer(craft);   // what clicking the recipe does
                me.temp_do_work = true;   // holding the work key
                Bot.Publish("craft", $"{station.unique_id}|{craft.id}|{string.Join(";", outIds)}");
                // A client first gets the station lease from the host; the craft starts after that.
                float s0 = UnityEngine.Time.realtimeSinceStartup;
                yield return Bot.WaitUntil("craft running", () => cc.is_crafting, 8f);
                Bot.Step($"info craft started={started} after {UnityEngine.Time.realtimeSinceStartup - s0:F2}s");
                Bot.Check("the station works on my recipe", cc.current_craft?.id == craft.id, cc.current_craft?.id ?? "none");
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                float lastLog = t0;
                while (cc.is_crafting && UnityEngine.Time.realtimeSinceStartup - t0 < 60f)
                {
                    if (UnityEngine.Time.realtimeSinceStartup - lastLog > 10f)
                    {
                        lastLog = UnityEngine.Time.realtimeSinceStartup;
                        Bot.Step($"info still crafting: progress={F(station.progress)} tool={me.GetEquippedTool()?.id} nearest={inter.nearest?.obj_id} energy={F(me.energy)}");
                    }
                    yield return null;
                }
                me.temp_do_work = false;
                Bot.Check("craft finished while working", !cc.is_crafting, $"after {UnityEngine.Time.realtimeSinceStartup - t0:F1}s");
                yield return Bot.Wait(2f);
                int got = haveOut() - outBefore + dropsAtStation() - dropsBefore;
                // An autopsy can fail (a surgeon's mistake destroys the organ): that is the game's
                // own outcome, not a sync problem.
                Func<string, int> mistakes = st =>
                {
                    var m = System.Text.RegularExpressions.Regex.Match(st, @"surgeon_mistakex(\d+)");
                    return m.Success ? int.Parse(m.Groups[1].Value) : 0;
                };
                bool surgeonMistake = got == 0 && station.is_autopsy_table &&
                                      mistakes(StationState(station)) > mistakes(stationBefore);
                if (surgeonMistake)
                    Bot.Step("info surgeon's mistake: the organ was destroyed (vanilla outcome)");
                else if (outIds.Count > 0)
                {
                    Bot.Check("crafted item received (inventory or ground)", got > 0, $"+{got}");
                    if (got <= 0)
                    {
                        // Where did it go? Every drop of it in the world, and every container holding it.
                        var drops = UnityEngine.Object.FindObjectsOfType<DropResGameObject>()
                            .Where(d => d != null && d.res != null && outIds.Any(o => d.res.id.StartsWith(o)))
                            .Select(d => $"{d.res.id}@{V(d.transform.position)} collected={d.is_collected}");
                        var holders = WorldMap.objs.Where(w => w != null && w.data != null &&
                                                              w.data.inventory.Any(i => i != null && outIds.Any(o => i.id.StartsWith(o + ":") || i.id == o)))
                                                   .Select(w => $"{w.obj_id}#{w.unique_id}");
                        Bot.Step($"info output {string.Join("/", outIds)}: drops [{string.Join(",", drops)}] held by [{string.Join(",", holders.Take(10))}] " +
                                 $"me at {V(MainGame.me.player.pos)} bag [{Items(MainGame.me.player.data.inventory)}]");
                    }
                }
                Bot.Step($"info station before [{stationBefore}] after [{StationState(station)}] params: {station.data.GetParam("quality", -1f)}");
                Bot.Publish("craft_changed", StationState(station) != stationBefore || got > 0 ? "1" : "0");
                Bot.Publish("craft_done", StationState(station));
                // Keep the published state current: an update already on its way can still
                // change the station here for a moment before the partner's echo arrives.
                float pub0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(Other, "craft_seen") != "1" && UnityEngine.Time.realtimeSinceStartup - pub0 < 40f)
                {
                    yield return Bot.Wait(1f);
                    Bot.Publish("craft_done", StationState(station));
                }
            }
            else
            {
                yield return Bot.WaitUntil("crafter started", () => Bot.Remote(crafter, "craft") != null, 40f);
                if (Bot.Remote(crafter, "craft") == "none") yield break;
                var p = Bot.Remote(crafter, "craft").Split('|');
                var station = WorldMap.GetWorldGameObjectByUniqueId(long.Parse(p[0]), false);
                Bot.Check("same station exists here", station?.components?.craft != null);
                if (station?.components?.craft == null) { Bot.Publish("craft_seen", "1"); yield break; }
                // Stand back: a partner next to the station picks up the crafter's output itself.
                var obs = MainGame.me.player;
                if (Vector2.Distance(obs.pos, station.pos) < 250f)
                {
                    obs.PlaceAtPos(station.pos + (obs.pos - station.pos).normalized * 300f + new Vector2(0f, -1f));
                    Bot.Step("info stepped back from the station");
                }
                bool sawCrafting = false;
                string sawCraft = null;
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(crafter, "craft_done") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 70f)
                {
                    if (station.components.craft.is_crafting) { sawCrafting = true; sawCraft = station.components.craft.current_craft?.id; }
                    yield return null;
                }
                Bot.Step($"info saw the station working={sawCrafting} craft={sawCraft} expected={p[1]}");
                Bot.Check("same recipe on the station", !sawCrafting || sawCraft == p[1], $"{sawCraft} vs {p[1]}");
                yield return Bot.WaitUntil("station idle again here", () => !station.components.craft.is_crafting, 10f);
                string theirs = Bot.Remote(crafter, "craft_done");
                Bot.Check("the work changed the station", Bot.Remote(crafter, "craft_changed") != "0", "crafter saw no change");
                yield return Bot.WaitUntil("station contents match the crafter's", () => StationState(station) == Bot.Remote(crafter, "craft_done"), 10f);
                theirs = Bot.Remote(crafter, "craft_done");
                Bot.Step($"info station here [{StationState(station)}] there [{theirs}]");
                Bot.Publish("craft_seen", "1");
            }
        }



        // ------------------------------------------------------------------ saving and the next evening
        private static string PersistFile(int player) =>
            System.IO.Path.Combine(System.IO.Directory.GetParent(Plugin.OutDir).FullName, $"persist_P{player}.txt");

        private static string Items(IEnumerable<Item> items) =>
            string.Join(",", items.Where(i => i != null && !i.IsEmpty()).GroupBy(i => i.id)
                                  .Select(g => g.Key + "x" + g.Sum(i => i.value)).OrderBy(x => x));

        private static Dictionary<string, string> PersonalState()
        {
            var me = MainGame.me.player;
            return new Dictionary<string, string>
            {
                ["bag"] = Items(me.data.inventory),
                ["toolbelt"] = Items(me.data.secondary_inventory),
                ["money"] = F(me.data.money),
                ["pos"] = V(me.pos),
                ["day"] = MainGame.me.save.day.ToString(),
            };
        }

        private static DateTime NewestWrite(string dir, string pattern) =>
            System.IO.Directory.Exists(dir)
                ? System.IO.Directory.GetFiles(dir, pattern).Select(System.IO.File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
                : DateTime.MinValue;

        /// <summary>End of an evening: the client picks something up, both go to bed (the game
        /// saves when the night ends), the bot writes down what was saved, then the host leaves
        /// with "Save and exit". The client must land back in the main menu.</summary>
        private static IEnumerator SessionEnd()
        {
            var me = MainGame.me.player;
            if (!Bot.IsHost)
            {
                string marker = new[] { "stick", "nails", "flour", "wheat_crop" }
                    .FirstOrDefault(id => GameBalance.me.GetDataOrNull<ItemDefinition>(id) != null);
                me.data.AddItem(marker, 3);   // stands in for something found that evening
                me.data.money += 1.25f;
                Bot.Step($"info picked up {marker}x3 and 1.25 money");
            }
            string profileDir = BepInEx.Paths.ConfigPath;
            string saveDir = UnityEngine.Application.persistentDataPath;
            DateTime profileBefore = NewestWrite(profileDir, "gkcoop_joiner_inventory_*");
            DateTime saveBefore = NewestWrite(saveDir, "*.dat");
            yield return Sleep(clientTired: true, wakeWithKey: false);
            if (Bot.IsHost)
                yield return Bot.WaitUntil("host save written after the night", () => NewestWrite(saveDir, "*.dat") > saveBefore, 30f);
            else
                yield return Bot.WaitUntil("joiner profile committed with the host save", () => NewestWrite(profileDir, "gkcoop_joiner_inventory_*") > profileBefore, 30f);

            var state = PersonalState();
            state["workers"] = WorkersDigest();
            if (Bot.IsHost)
            {
                var l1 = MainGame.me.save.dungeons.GetSavedDungeon(1);
                state["dungeon1_alive"] = l1 == null || l1.objects == null || l1.objects.Count == 0 ? "fresh"
                    : l1.objects.Count(o => o.type == DungeonGenerator.SavedDungeonObject.SavedDungeonObjectType.Mob && o.mob_is_alive).ToString();
            }
            if (Bot.IsHost)
                foreach (var uid in Diag.Destroyed)
                    state["obj" + uid] = WorldMap.GetWorldGameObjectByUniqueId(uid, false)?.obj_id ?? "none";
            System.IO.File.WriteAllLines(PersistFile(Plugin.Player), state.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            Bot.Step("info saved state: " + string.Join(" ", state.Select(kv => kv.Key + "=" + kv.Value)));
            yield return Bot.Sync("state_written");

            if (Bot.IsHost)
            {
                yield return Bot.Wait(1f);
                Bot.Step("info host: Save and exit -> Yes");
                GUIElements.me.ingame_menu.ReturnToMainMenu();   // what the dialog's Yes does
                yield return Bot.WaitUntil("host back in the main menu", () => GUIElements.me.main_menu.is_shown, 30f);
            }
            else
            {
                var dialog = GUIElements.me.dialog;
                yield return Bot.WaitUntil("client is told the host left", () => dialog.is_shown, 30f);
                string text = dialog.label_1?.text ?? "";
                Bot.Check("message says the host left", text.ToLowerInvariant().Contains("host"), $"'{text}'");
                yield return Bot.Wait(1f);
                // Click OK, like the player.
                var buttons = HarmonyLib.AccessTools.Field(typeof(DialogGUI), "_dialog_buttons").GetValue(dialog);
                var delegates = (System.Collections.IList)HarmonyLib.AccessTools.Field(buttons.GetType(), "_delegates").GetValue(buttons);
                ((GJCommons.VoidDelegate)delegates[0]).Invoke();
                yield return Bot.WaitUntil("client back in the main menu after OK",
                    () => GUIElements.me.main_menu.is_shown && !MainGame.game_started && !LoadingGUI.is_shown, 60f);
                Bot.Check("no window left open", BaseGUI.opened_windows.Count == 0 || BaseGUI.opened_windows.All(w => w == GUIElements.me.main_menu),
                          string.Join(",", BaseGUI.opened_windows.Select(w => w.GetType().Name)));
            }
        }


        /// <summary>Mid-game drop: the friend leaves to the main menu, the host keeps playing,
        /// invites again, and the friend comes back into the running game with their things.</summary>
        private static IEnumerator Rejoin()
        {
            var ocm = OnlineCoopManager.Instance;
            if (!Bot.IsHost)
            {
                var me = MainGame.me.player;
                me.data.AddItem("stick", 2);
                me.data.money += 0.5f;
                var before = PersonalState();
                Bot.Step("info leaving with: " + string.Join(" ", before.Select(kv => kv.Key + "=" + kv.Value)));
                Bot.LastInviteLobby = 0;
                Bot.Publish("leaving", "1");
                yield return Bot.Wait(1f);
                GUIElements.me.ingame_menu.ReturnToMainMenu();
                yield return Bot.WaitUntil("friend back in the main menu",
                    () => GUIElements.me.main_menu.is_shown && !MainGame.game_started && !LoadingGUI.is_shown, 60f);
                Bot.Publish("in_menu", "1");
                yield return Bot.WaitUntil("invited again", () => Bot.LastInviteLobby != 0, 90f);
                yield return Bot.Wait(1.5f);
                Bot.Call(SteamLobbyManager.Instance, "AcceptInvite", new Steamworks.CSteamID(Bot.LastInviteLobby), "QA-P1");
                bool pressedReady = false;
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (!Tests.InSession() && UnityEngine.Time.realtimeSinceStartup - t0 < 240f)
                {
                    if (!pressedReady && GraveyardKeeperCoop.UI.LobbyGUI.Instance != null && GraveyardKeeperCoop.UI.LobbyGUI.Instance.is_shown)
                    {
                        yield return Bot.Wait(2f);
                        Bot.Step("info lobby screen shown on rejoin; pressing Ready");
                        Bot.Call(GraveyardKeeperCoop.UI.LobbyGUI.Instance, "OnReadyPressed");
                        pressedReady = true;
                    }
                    yield return null;
                }
                Bot.Check("back in the running game", Tests.InSession(), $"after {UnityEngine.Time.realtimeSinceStartup - t0:F0}s");
                if (!Tests.InSession()) { Bot.Publish("rejoined", "fail"); yield break; }
                yield return Bot.Wait(3f);
                me = MainGame.me.player;
                var after = PersonalState();
                Bot.Step("info back with: " + string.Join(" ", after.Select(kv => kv.Key + "=" + kv.Value)));
                Bot.Publish("back", "1");
                yield return Bot.WaitUntil("host told its day", () => Bot.Remote(1, "host_day_now") != null, 20f);
                Bot.Check("same day as the host", MainGame.me.save.day.ToString() == Bot.Remote(1, "host_day_now"),
                          $"{MainGame.me.save.day} vs {Bot.Remote(1, "host_day_now")}");
                // By design the personal state is committed only with a host save (sleep), so what
                // was picked up since then is rolled back on rejoin. Report it, do not fail on it.
                Bot.Step(after["bag"] == before["bag"] && after["money"] == before["money"]
                    ? "info bag and money kept"
                    : $"info rolled back to the last host save: bag [{before["bag"]}] -> [{after["bag"]}], money {before["money"]} -> {after["money"]}");
                Bot.Check("toolbelt kept", after["toolbelt"] == before["toolbelt"], $"left [{before["toolbelt"]}] back [{after["toolbelt"]}]");
                Bot.Publish("rejoined", "1");
                // A player needs a few seconds to walk anywhere; meanwhile the host's full
                // world state (several hundred KB) finishes arriving.
                yield return Bot.Wait(10f);
                yield return Bot.Sync("rejoin_done");
            }
            else
            {
                yield return Bot.WaitRemote(2, "leaving", "1", 30f);
                yield return Bot.WaitUntil("host notices the friend left", () => ocm.RemotePlayerCount == 0, 30f);
                Bot.Check("host still in the game", MainGame.game_started && MainGame.me?.player != null && !GUIElements.me.main_menu.is_shown);
                yield return Bot.WaitRemote(2, "in_menu", "1", 60f);
                // Keep playing a little while alone.
                var me = MainGame.me.player;
                me.PlaceAtPos(me.pos + new Vector2(96f, 0f));
                yield return Bot.Wait(3f);
                Bot.Check("host still in a lobby to invite into", SteamLobbyManager.Instance.IsInLobby);
                Bot.Require("invite sent again", SteamLobbyManager.Instance.InviteFriend(new Steamworks.CSteamID(Bot.IdOf(2))));
                yield return Bot.WaitUntil("friend back in the game", () => Bot.Remote(2, "back") != null || Bot.Remote(2, "rejoined") != null, 300f);
                Bot.Publish("host_day_now", MainGame.me.save.day.ToString());
                yield return Bot.WaitUntil("friend rejoined", () => Bot.Remote(2, "rejoined") != null, 60f);
                Bot.Check("friend is back in the game", Bot.Remote(2, "rejoined") == "1");
                if (Bot.Remote(2, "rejoined") != "1") yield break;
                yield return Bot.WaitUntil("host sees the friend again", () => ocm.RemotePlayerCount == 1, 30f);
                yield return Bot.Sync("rejoin_done");
            }
        }

        private static Dictionary<string, string> ReadState(int player)
        {
            var f = PersistFile(player);
            if (!System.IO.File.Exists(f)) return null;
            return System.IO.File.ReadAllLines(f).Where(l => l.Contains("="))
                .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
        }

        /// <summary>The next evening (run with --keep): the host loads the saved coop game, the
        /// friend joins, and everything written down at the end of the last evening is back.</summary>
        private static IEnumerator ResumeCheck()
        {
            var was = ReadState(Plugin.Player);
            Bot.Check("state from the last evening exists", was != null, PersistFile(Plugin.Player));
            if (was == null) yield break;
            var now = PersonalState();
            Bot.Step("info now: " + string.Join(" ", now.Select(kv => kv.Key + "=" + kv.Value)));
            Bot.Check("same day as saved", now["day"] == was["day"], $"{now["day"]} vs {was["day"]}");
            if (!Bot.IsHost)
            {
                Bot.Check("bag restored", now["bag"] == was["bag"], $"now [{now["bag"]}] saved [{was["bag"]}]");
                Bot.Check("toolbelt restored", now["toolbelt"] == was["toolbelt"], $"now [{now["toolbelt"]}] saved [{was["toolbelt"]}]");
                Bot.Check("money restored", Mathf.Abs(PF(now["money"]) - PF(was["money"])) < 0.01f, $"{now["money"]} vs {was["money"]}");
            }
            float moved = Vector2.Distance(ParseV(now["pos"]), ParseV(was["pos"])) / 96f;
            Bot.Check("starts where the last evening ended (within 3 tiles)", moved < 3f, $"{moved:F1} tiles away");
            if (Bot.IsHost)
                foreach (var kv in was.Where(k => k.Key.StartsWith("obj")))
                {
                    long uid = long.Parse(kv.Key.Substring(3));
                    string here = WorldMap.GetWorldGameObjectByUniqueId(uid, false)?.obj_id ?? "none";
                    Bot.Check($"world object #{uid} as saved", here == kv.Value, $"{here} vs {kv.Value}");
                }
            // Zombie workers: where they are and whether they work, the same as saved and on both sides.
            string workers = WorkersDigest();
            Bot.Step("info workers now: " + workers);
            if (was.ContainsKey("workers"))
                Bot.Check("zombie workers as saved", workers == was["workers"], $"now [{workers}] saved [{was["workers"]}]");
            Bot.Publish("resume_workers", workers);
            yield return Bot.WaitUntil("other side's workers", () => Bot.Remote(Other, "resume_workers") != null, 30f);
            Bot.Check("zombie workers the same on both sides", Bot.Remote(Other, "resume_workers") == workers,
                      $"here [{workers}] there [{Bot.Remote(Other, "resume_workers")}]");

            // The dungeon: the monsters killed last evening stay dead, both see the same ones.
            Bot.KeepAlive = true;
            if (Bot.IsHost)
            {
                MainGame.me.TeleportToDungeonLevel(1);
                yield return Bot.WaitUntil("in the dungeon", () => DungeonLevel() == 1, 20f);
                yield return Bot.WaitUntil("can move", CanMove, 15f);
                yield return Bot.Wait(4f);
                Bot.Publish("resume_dungeon", "1");
            }
            else
            {
                yield return Bot.WaitUntil("host in the dungeon", () => Bot.Remote(1, "resume_dungeon") != null, 60f);
                yield return Bot.WaitUntil("taken along into the dungeon", () => DungeonLevel() == 1, 30f);
                yield return Bot.WaitUntil("can move", CanMove, 15f);
                yield return Bot.Wait(4f);
            }
            if (Bot.IsHost && was.TryGetValue("dungeon1_alive", out var alive) && alive != "fresh")
                Bot.Check("dungeon level 1: only the monsters alive last evening", DungeonMobs().Count.ToString() == alive,
                          $"{DungeonMobs().Count} monsters, {alive} were alive");
            yield return SameMobs("resume_mobs", "dungeon level 1: the same monsters on both sides");
            if (Bot.IsHost)
            {
                yield return Bot.WaitRemote(2, "resume_ready_out", "1", 30f);
                var exit = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id == "dungeon_exit")
                                        .OrderBy(w => (w.pos - MainGame.me.player.pos).sqrMagnitude).FirstOrDefault();
                if (exit != null)
                {
                    yield return WalkUpTo(exit, 120f);
                    yield return FakeInput.Press(GameKey.Interaction);
                }
                yield return Bot.WaitUntil("left the dungeon", () => !InDungeon(), 20f);
                Bot.Publish("resume_out", "1");
            }
            else
            {
                Bot.Publish("resume_ready_out", "1");
                yield return Bot.WaitUntil("host walked out", () => Bot.Remote(1, "resume_out") != null, 90f);
                yield return Bot.WaitUntil("taken out of the dungeon too", () => !InDungeon(), 20f);
            }

            string mine = MyWorld();
            Bot.Publish("resume_world", mine);
            yield return Bot.WaitUntil("other side reported its world", () => Bot.Remote(Other, "resume_world") != null, 30f);
            Bot.Check("both sides load the same stumps", Bot.Remote(Other, "resume_world") == mine,
                      $"here [{mine}] there [{Bot.Remote(Other, "resume_world")}]");
        }

        /// <summary>Zombie workers: kind, place (tiles), working or in stock, and the bench they serve.</summary>
        private static string WorkersDigest() =>
            string.Join(";", WorldMap.objs.Where(w => w != null && !w.is_removed && w.worker != null)
                .Select(w =>
                {
                    var bench = WorldMap.objs.FirstOrDefault(b => b != null && !b.is_removed && b.linked_worker == w);
                    return $"{w.unique_id}:{w.obj_id}@{Mathf.RoundToInt(w.pos.x / 96f)},{Mathf.RoundToInt(w.pos.y / 96f)}" +
                           $" disabled={w.GetParamInt("is_disabled")} bench={(bench == null ? "-" : bench.unique_id.ToString())}";
                })
                .OrderBy(x => x));

        private static string MyWorld() =>
            string.Join(";", WorldMap.objs.Where(w => w != null && w.unique_id > 0 && w.obj_id != null && w.obj_id.Contains("stump"))
                                          .Select(w => w.unique_id + ":" + w.obj_id).OrderBy(x => x).Take(50));


        // ------------------------------------------------------------------ talking to NPCs
        private static readonly List<string> bubblesSeen = new List<string>();
        private static bool bubbleHookInstalled;

        private static void InstallBubbleHook()
        {
            if (bubbleHookInstalled) return;
            bubbleHookInstalled = true;
            var m = HarmonyLib.AccessTools.Method(typeof(SpeechBubbleGUI), nameof(SpeechBubbleGUI.ShowMessage),
                new[] { typeof(long), typeof(string), typeof(Transform), typeof(GJCommons.VoidDelegate), typeof(bool), typeof(bool),
                        typeof(SpeechBubbleGUI.SpeechBubbleType), typeof(bool), typeof(SmartSpeechEngine.VoiceID) });
            new HarmonyLib.Harmony("aaa.coopqa.bubbles").Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(SyncTests), nameof(BubbleShown)));
        }

        private static void BubbleShown(long speaker_id, string txt, bool is_player)
        {
            var w = speaker_id > 0 ? WorldMap.GetWorldGameObjectByUniqueId(speaker_id, false) : null;
            bubblesSeen.Add($"{(is_player ? "player" : w?.obj_id ?? speaker_id.ToString())}: {txt}");
        }

        private static SpeechBubbleGUI ActiveBubble() =>
            SpeechBubbleGUI.all.Values.FirstOrDefault(b => b != null && b.gameObject.activeInHierarchy &&
                                                           !(bool)HarmonyLib.AccessTools.Field(typeof(SpeechBubbleGUI), "_disappearing").GetValue(b));

        private static List<MultiAnswerOptionGUI> OpenAnswers()
        {
            var cur = HarmonyLib.AccessTools.Field(typeof(MultiAnswerGUI), "_current").GetValue(null) as MultiAnswerGUI;
            if (cur == null || !cur.gameObject.activeInHierarchy) return null;
            var list = HarmonyLib.AccessTools.Field(typeof(MultiAnswerGUI), "_answers").GetValue(cur) as List<MultiAnswerOptionGUI>;
            return list != null && list.Count > 0 && list.Any(a => a != null) ? list : null;
        }

        private static bool CanMove()
        {
            var pc = MainGame.me.player_char;
            return !(bool)HarmonyLib.AccessTools.Method(typeof(BaseCharacterComponent), "PlayerControlIsDisabled").Invoke(pc, null);
        }

        /// <summary>One player walks up to an NPC and talks (E), clicks through the lines and
        /// picks the first answer; the other stands next to them. Both must see the same lines,
        /// and afterwards nobody may be stuck in a dialogue.</summary>
        private static IEnumerator NpcTalk(int talker)
        {
            InstallBubbleHook();
            var me = MainGame.me.player;
            WorldGameObject npc;
            if (Plugin.Player == talker)
            {
                var npcs = WorldMap.objs.Where(w => w != null && !w.is_player && w.unique_id > 0 && w.obj_def != null && w.obj_def.IsNPC() &&
                                                    w.gameObject.activeInHierarchy && !string.IsNullOrEmpty(w.obj_def.attached_script))
                                        .OrderBy(w => (w.pos - me.pos).sqrMagnitude).ToList();
                Bot.Step("info NPCs: " + string.Join(",", npcs.Take(10).Select(w => $"{w.obj_id}#{w.unique_id}@{F((w.pos - me.pos).magnitude / 96f)}t")));
                string prev = Bot.RemoteIn(1, "npc_talk_host", "npc");
                npc = npcs.FirstOrDefault(w => prev == null || !prev.StartsWith(w.unique_id + "|")) ?? npcs.FirstOrDefault();
                Bot.Check("found an NPC to talk to", npc != null);
                if (npc == null) { Bot.Publish("npc", "none"); yield break; }
                Bot.Publish("npc", $"{npc.unique_id}|{npc.obj_id}");
            }
            else
            {
                yield return Bot.WaitUntil("talker picked an NPC", () => Bot.Remote(talker, "npc") != null, 30f);
                if (Bot.Remote(talker, "npc") == "none") yield break;
                npc = WorldMap.GetWorldGameObjectByUniqueId(long.Parse(Bot.Remote(talker, "npc").Split('|')[0]), false);
                Bot.Check("same NPC here", npc != null);
                if (npc == null) { Bot.Publish("talk_done", "1"); yield break; }
                me.PlaceAtPos(npc.pos + new Vector2(0f, -250f));   // stand close by
            }
            bubblesSeen.Clear();
            yield return Bot.Sync("npc_ready");

            if (Plugin.Player == talker)
            {
                yield return WalkUpTo(npc, 150f);
                Bot.Step($"info nearest={me.components.interaction.nearest?.obj_id} npc={npc.obj_id}");
                yield return FakeInput.Press(GameKey.Interaction);   // E
                var choices = new List<string>();
                float quiet = UnityEngine.Time.realtimeSinceStartup, t0 = quiet;
                int presses = 0;
                float lastNote = 0f, windowSince = -1f;
                while (UnityEngine.Time.realtimeSinceStartup - quiet < 4f && UnityEngine.Time.realtimeSinceStartup - t0 < 60f)
                {
                    var answers = OpenAnswers();
                    if (answers != null)
                    {
                        yield return Bot.Wait(0.8f);
                        // Like a player: skip locked answers (price not affordable) and trading.
                        var pickF = HarmonyLib.AccessTools.Field(typeof(MultiAnswerOptionGUI), "_can_be_picked");
                        var dataF = HarmonyLib.AccessTools.Field(typeof(MultiAnswerOptionGUI), "_answer_data");
                        var pickable = answers.Where(a => a != null && (bool)pickF.GetValue(a)).ToList();
                        Func<MultiAnswerOptionGUI, string> idOf = a => (dataF.GetValue(a) as AnswerVisualData)?.id ?? "";
                        // Ask each question once, then say goodbye.
                        var first = pickable.FirstOrDefault(a => !idOf(a).Contains("trade") && idOf(a) != "Leave" && !choices.Contains(idOf(a)))
                                    ?? pickable.FirstOrDefault(a => idOf(a) == "Leave")
                                    ?? pickable.FirstOrDefault() ?? answers.First(a => a != null);
                        var data = dataF.GetValue(first) as AnswerVisualData;
                        choices.Add(data?.id ?? first.name);
                        if (choices.Count <= 6)
                            Bot.Step($"info answers offered: {string.Join(" / ", answers.Where(a => a != null).Select(a => (HarmonyLib.AccessTools.Field(typeof(MultiAnswerOptionGUI), "_answer_data").GetValue(a) as AnswerVisualData)?.id))} -> picking {data?.id}");
                        first.OnChosen();   // clicking the first answer
                        quiet = UnityEngine.Time.realtimeSinceStartup;
                        yield return Bot.Wait(0.5f);
                        continue;
                    }
                    if (ActiveBubble() != null)
                    {
                        yield return Bot.Wait(0.7f);
                        yield return FakeInput.Press(GameKey.Select);   // click through the line
                        presses++;
                        quiet = UnityEngine.Time.realtimeSinceStartup;
                        continue;
                    }
                    if (BaseGUI.opened_windows.Count == 0) windowSince = -1f;
                    else if (windowSince < 0f) windowSince = UnityEngine.Time.realtimeSinceStartup;
                    if (windowSince > 0f && UnityEngine.Time.realtimeSinceStartup - windowSince > 2f)
                    {
                        windowSince = -1f;
                        Bot.Step("info closing window with Esc: " + string.Join(",", BaseGUI.opened_windows.Select(w => w.GetType().Name)));
                        yield return FakeInput.Press(GameKey.Back);
                        quiet = UnityEngine.Time.realtimeSinceStartup;
                        continue;
                    }
                    if (!CanMove())
                    {
                        quiet = UnityEngine.Time.realtimeSinceStartup;   // a script is still running
                        if (UnityEngine.Time.realtimeSinceStartup - lastNote > 5f)
                        {
                            lastNote = UnityEngine.Time.realtimeSinceStartup;
                            var pc = MainGame.me.player_char;
                            Bot.Step($"info cannot move: windows=[{string.Join(",", BaseGUI.opened_windows.Select(w => w.GetType().Name))}] control={pc.control_enabled} " +
                                     $"building={MainGame.me.build_mode_logics.IsBuilding()} synced_dialogue={GraveyardKeeperCoop.Multiplayer.DialogueSync.Instance?.IsInSyncedDialogue} bubbles={SpeechBubbleGUI.all.Count}");
                        }
                    }
                    yield return null;
                }
                Bot.Step($"info talk: {bubblesSeen.Count} lines, {presses} clicks, {choices.Count} choices");
                foreach (var b in bubblesSeen.Take(12)) Bot.Step("     line " + b);
                Bot.Step(bubblesSeen.Count > 0 ? "info the NPC talked" : "info the NPC had nothing to say (villager/guard)");
                Bot.Publish("talk_done", string.Join("\u0001", bubblesSeen));
            }
            else
            {
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(talker, "talk_done") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 80f) yield return null;
                yield return Bot.Wait(2f);
                var theirs = (Bot.Remote(talker, "talk_done") ?? "").Split('\u0001').Where(x => x.Length > 0).ToList();
                Bot.Step($"info lines here {bubblesSeen.Count}, there {theirs.Count}");
                foreach (var b in bubblesSeen.Take(12)) Bot.Step("     seen " + b);
                Func<string, string> text = l => l.Substring(l.IndexOf(": ") + 2);
                var mine = bubblesSeen.Select(text).ToList();
                var missing = theirs.Select(text).Where(l => !mine.Contains(l)).ToList();
                Bot.Check("saw the same conversation", missing.Count == 0, missing.Count == 0 ? "" : "missing: " + string.Join(" | ", missing.Take(5)));
            }
            yield return Bot.Sync("talk_over");
            yield return Bot.WaitUntil("can move again after the conversation", CanMove, 6f);
            var ds = GraveyardKeeperCoop.Multiplayer.DialogueSync.Instance;
            yield return Bot.WaitUntil("not stuck in a synced dialogue", () => ds == null || !ds.IsInSyncedDialogue, 6f);
            Bot.Check("no answer menu left open", OpenAnswers() == null);
        }



        // ------------------------------------------------------------------ trading
        private static string VendorState(WorldGameObject w) =>
            w?.data == null ? "none" : $"money={F(w.data.GetParam("money"))} stock=[{Items(w.data.inventory)}]";

        /// <summary>Buy one item from a vendor the way VendorGUI does (move it into the vendor's
        /// offer, accept). The buyer pays and gets it; the vendor looks the same on both sides
        /// and stays that way; the partner's money does not change.</summary>
        private static IEnumerator Trade(int buyer)
        {
            var me = MainGame.me.player;
            float myMoney0 = me.data.money;
            Bot.Publish("money0_" + buyer, F(myMoney0));
            if (Plugin.Player == buyer)
            {
                WorldGameObject v = null; Item ware = null; float price = 0f;
                foreach (var w in WorldMap.objs.Where(w => w != null && w.unique_id > 0 && !w.is_player && w.obj_def != null &&
                                                           GameBalance.me.GetDataOrNull<VendorDefinition>(w.obj_id) != null)
                                               .OrderBy(w => (w.pos - me.pos).sqrMagnitude))
                {
                    var vend = w.vendor;
                    if (vend == null) continue;
                    vend.FillDrawingMultiInventory();
                    ware = w.data.inventory.FirstOrDefault(i => i != null && !i.IsEmpty() && i.value > 0 && vend.CanSellItem(i) &&
                                                                 vend.GetSingleItemPrice(i) > 0f);
                    if (ware != null) { v = w; price = vend.GetSingleItemPrice(ware); break; }
                }
                Bot.Check("found a vendor with something to sell", v != null);
                if (v == null) { Bot.Publish("trade", "none"); yield break; }
                if (me.data.money < price * 2f + 1f) me.data.money += price * 2f + 1f;   // pocket money
                float money0 = me.data.money;
                string wareId = ware.id;
                int have0 = me.data.GetTotalCount(wareId, true);
                string vendor0 = VendorState(v);
                Bot.Step($"info buying 1 {wareId} from {v.obj_id}#{v.unique_id} for ~{F(price)}; vendor [{vendor0}] my money {F(money0)}");
                // Stand at the vendor like a player does (the game only runs objects near the player).
                yield return WalkUpTo(v, 150f);
                yield return Bot.Wait(1f);
                Bot.Step($"info at the vendor: active={v.gameObject.activeInHierarchy} dist={F((v.pos - me.pos).magnitude / 96f)}t");
                Bot.Publish("trade", $"{v.unique_id}|{wareId}|{F(money0)}");
                yield return Bot.WaitRemote(Other, "trade_ready", "1", 30f);

                var t = new Trading(v);
                t.trader.cur_offer.SetInventorySize(6);
                t.trader.inventory.RemoveItem(new Item(wareId, 1), 1, MultiInventory.DestinationType.AllFromFirst);
                t.trader.cur_offer.AddItem(wareId, 1);
                float balance = t.GetTotalBalance();
                bool can = t.CanAcceptOffer();
                t.DoAcceptOffer();
                Bot.Check("the vendor accepted the offer", can, $"balance {F(balance)}");
                Bot.Check("paid for it", Mathf.Abs(me.data.money - (money0 + balance)) < 0.02f, $"{F(money0)} -> {F(me.data.money)} (balance {F(balance)})");
                Bot.Check("got the item", me.data.GetTotalCount(wareId, true) == have0 + 1, $"{have0} -> {me.data.GetTotalCount(wareId, true)}");
                string vendor1 = VendorState(v);
                Bot.Step($"info vendor after [{vendor1}]");
                Bot.Check("the vendor changed", vendor1 != vendor0);
                Bot.Publish("trade_done", vendor1);
                yield return Bot.WaitRemote(Other, "trade_seen", "1", 40f);
                // A stale host snapshot must not undo the purchase on either side.
                yield return Bot.Wait(6f);
                Bot.Check("the purchase stays here (vendor)", VendorState(v) == vendor1, $"now [{VendorState(v)}]");
                Bot.Check("the purchase stays here (item and money)",
                          me.data.GetTotalCount(wareId, true) == have0 + 1 && Mathf.Abs(me.data.money - (money0 + balance)) < 0.02f,
                          $"items {me.data.GetTotalCount(wareId, true)}, money {F(me.data.money)}");
                Bot.Publish("trade_final", VendorState(v));
                yield return Bot.WaitRemote(Other, "trade_final_seen", "1", 30f);
            }
            else
            {
                yield return Bot.WaitUntil("buyer picked a vendor", () => Bot.Remote(buyer, "trade") != null, 40f);
                if (Bot.Remote(buyer, "trade") == "none") yield break;
                var p = Bot.Remote(buyer, "trade").Split('|');
                var v = WorldMap.GetWorldGameObjectByUniqueId(long.Parse(p[0]), false);
                Bot.Check("the vendor exists here", v != null);
                Bot.Step($"info vendor here before [{VendorState(v)}]");
                Bot.Publish("trade_ready", "1");
                yield return Bot.WaitUntil("buyer finished", () => Bot.Remote(buyer, "trade_done") != null, 40f);
                string theirs = Bot.Remote(buyer, "trade_done");
                yield return Bot.WaitUntil("the vendor looks the same here", () => VendorState(v) == theirs, 15f);
                Bot.Step($"info vendor here [{VendorState(v)}] there [{theirs}]");
                Bot.Publish("trade_seen", "1");
                yield return Bot.WaitUntil("buyer checked it stays", () => Bot.Remote(buyer, "trade_final") != null, 30f);
                yield return Bot.Wait(1f);
                Bot.Check("the vendor still looks the same here", VendorState(v) == Bot.Remote(buyer, "trade_final"),
                          $"here [{VendorState(v)}] there [{Bot.Remote(buyer, "trade_final")}]");
                Bot.Check("my money did not change", Mathf.Abs(me.data.money - myMoney0) < 0.02f, $"{F(myMoney0)} -> {F(me.data.money)}");
                Bot.Publish("trade_final_seen", "1");
            }
        }




        // ------------------------------------------------------------------ dungeon
        private static string DungeonInfo()
        {
            var root = MainGame.me.dungeon_root;
            var save = MainGame.me.save;
            var me = MainGame.me.player;
            var near = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && Vector2.Distance(w.pos, me.pos) < 1500f)
                                    .GroupBy(w => w.obj_id).Select(g => $"{g.Key}x{g.Count()}");
            return $"loaded={root.dungeon_is_loaded_now} level={root.cur_dungeon_preset?.dungeon_level} seed={save.dungeon_seed} " +
                   $"completed={root.cur_saved_dungeon?.is_completed} pos={V(me.pos)} near: {string.Join(",", near.Take(40))}";
        }

        /// <summary>Explore: the host enters dungeon level 1, both report what they see.</summary>
        private static IEnumerator DungeonProbe()
        {
            var me = MainGame.me.player;
            Bot.Step("info before: " + DungeonInfo());
            Bot.Step("info dungeon objects in the world: " + string.Join(",", WorldMap.objs.Where(w => w != null && w.obj_id != null && w.obj_id.Contains("dung")).Select(w => $"{w.obj_id}@{V(w.pos)}").Take(10)));
            if (Bot.IsHost)
            {
                MainGame.me.TeleportToDungeonLevel(1);
                yield return Bot.Wait(5f);
                Bot.Step("info in dungeon: " + DungeonInfo());
                Bot.Publish("dungeon_in", "1");
                yield return Bot.Wait(10f);
            }
            else
            {
                yield return Bot.WaitUntil("host entered", () => Bot.Remote(1, "dungeon_in") != null, 60f);
                yield return Bot.Wait(3f);
                var host = OnlineCoopManager.Instance?.GetRemotePlayer();
                Bot.Step($"info host proxy at {(host == null ? "none" : V(host.transform.position) + " active=" + host.gameObject.activeInHierarchy)}");
                Bot.Step("info client: " + DungeonInfo());
                MainGame.me.TeleportToDungeonLevel(1);
                yield return Bot.Wait(5f);
                Bot.Step("info client in dungeon: " + DungeonInfo());
                host = OnlineCoopManager.Instance?.GetRemotePlayer();
                Bot.Step($"info host proxy at {(host == null ? "none" : V(host.transform.position) + " active=" + host.gameObject.activeInHierarchy)}");
            }
        }


        private static bool InDungeon() => MainGame.me.dungeon_root != null && MainGame.me.dungeon_root.dungeon_is_loaded_now;

        /// <summary>The dungeon's objects (rooms, furniture, exits), by kind: both peers generate them from one seed.</summary>
        private static string DungeonObjects()
        {
            var root = MainGame.me.dungeon_root;
            if (root == null || !root.dungeon_is_loaded_now) return "";
            var all = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && !w.is_player &&
                                               Vector2.Distance(w.pos, root.enter_to_dunge.transform.position) < 6000f &&
                                               (w.obj_def == null || !w.obj_def.IsNPC()) && !w.obj_id.StartsWith("bat") && !w.obj_id.StartsWith("slime") &&
                                               !w.obj_id.StartsWith("skeleton") && !w.obj_id.StartsWith("zombie"));
            return string.Join(",", all.GroupBy(w => w.obj_id).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}"));
        }

        /// <summary>Why a dungeon object is counted twice: listed twice, or two real objects (where).</summary>
        private static string DungeonDuplicates()
        {
            var root = MainGame.me.dungeon_root;
            var list = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id == "dungeon_exit").ToList();
            var distinct = list.Distinct().ToList();
            return $"dungeon_exit listed {list.Count}, distinct {distinct.Count}: " +
                   string.Join(" ", distinct.Select(w => $"#{w.unique_id}@{V(w.pos)} active={w.gameObject.activeInHierarchy} " +
                                                         $"underRoot={w.transform.IsChildOf(root.transform)} parent={w.transform.parent?.name}"));
        }

        /// <summary>The host goes down into the dungeon; the partner is taken along, gets the same
        /// level with every object once, both see each other; the host walks out, the partner too.</summary>
        private static IEnumerator Dungeon()
        {
            var me = MainGame.me.player;
            if (Bot.IsHost)
            {
                Bot.Check("not in a dungeon yet", !InDungeon());
                MainGame.me.TeleportToDungeonLevel(1);   // what the level button at the dungeon entrance does
                yield return Bot.WaitUntil("in the dungeon", InDungeon, 15f);
                yield return Bot.Wait(4f);
                Bot.Publish("dungeon_objs", DungeonObjects());
                Bot.Publish("dungeon_in", $"{MainGame.me.dungeon_root.cur_dungeon_preset.dungeon_level}|{MainGame.me.save.dungeon_seed}");
                yield return Bot.WaitRemote(2, "dungeon_checked", "1", 60f);
                var exit = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id == "dungeon_exit")
                                        .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("found the dungeon exit", exit != null);
                if (exit != null)
                {
                    yield return WalkUpTo(exit, 120f);
                    Bot.Step($"info at the exit: nearest={me.components.interaction.nearest?.obj_id}");
                    yield return FakeInput.Press(GameKey.Interaction);
                    yield return Bot.WaitUntil("left the dungeon", () => !InDungeon(), 20f);
                    if (InDungeon()) Bot.Step("info still inside: " + DungeonInfo());
                }
                yield return Bot.WaitUntil("can move again", CanMove, 10f);
                Bot.Publish("dungeon_out", "1");
                yield return Bot.WaitRemote(2, "dungeon_done", "1", 60f);
            }
            else
            {
                yield return Bot.WaitUntil("host in the dungeon", () => Bot.Remote(1, "dungeon_in") != null, 60f);
                var p = Bot.Remote(1, "dungeon_in").Split('|');
                yield return Bot.WaitUntil("taken along into the dungeon", InDungeon, 20f);
                yield return Bot.Wait(4f);
                Bot.Check("same level and seed", InDungeon() && MainGame.me.dungeon_root.cur_dungeon_preset.dungeon_level.ToString() == p[0] &&
                          MainGame.me.save.dungeon_seed.ToString() == p[1], $"here {MainGame.me.dungeon_root.cur_dungeon_preset?.dungeon_level}|{MainGame.me.save.dungeon_seed}, host {p[0]}|{p[1]}");
                string mine = DungeonObjects(), host = Bot.Remote(1, "dungeon_objs");
                Bot.Check("the same dungeon objects, each once", mine == host, $"here [{mine}] host [{host}]");
                if (mine != host) Bot.Step("info " + DungeonDuplicates());
                var proxy = OnlineCoopManager.Instance?.GetRemotePlayer();
                Bot.Check("I see the host in the dungeon", proxy != null && proxy.gameObject.activeInHierarchy &&
                          Vector2.Distance(proxy.transform.position, me.pos) < 2000f, proxy == null ? "none" : V(proxy.transform.position) + " me " + V(me.pos));
                Bot.Check("I can move in the dungeon", CanMove());
                Bot.Publish("dungeon_checked", "1");
                yield return Bot.WaitUntil("host walked out", () => Bot.Remote(1, "dungeon_out") != null, 60f);
                yield return Bot.WaitUntil("taken out of the dungeon too", () => !InDungeon(), 20f);
                yield return Bot.WaitUntil("can move again", CanMove, 10f);
                Bot.Publish("dungeon_done", "1");
            }
        }

        private static List<WorldGameObject> DungeonMobs()
        {
            var root = MainGame.me.dungeon_root;
            if (root == null || !root.dungeon_is_loaded_now) return new List<WorldGameObject>();
            return WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_def != null && w.hp > 0f &&
                                            w.obj_def.type == ObjectDefinition.ObjType.Mob &&
                                            w.transform.IsChildOf(root.transform)).ToList();
        }

        private static string MobList(IEnumerable<WorldGameObject> mobs) =>
            string.Join(",", mobs.OrderBy(w => w.unique_id).Select(w => $"{w.obj_id}#{w.unique_id}@{V(w.pos)}"));

        private static string MobKinds(IEnumerable<WorldGameObject> mobs) =>
            string.Join(",", mobs.OrderBy(w => w.unique_id).Select(w => $"{w.obj_id}#{w.unique_id % 1000000}"));

        /// <summary>The same monster on the other side: the same id, else the same kind nearest.</summary>
        private static WorldGameObject MatchMob(string ann)
        {
            var p = ann.Split('|');
            long uid = long.Parse(p[0]);
            var same = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
            if (same != null && same.obj_id == p[1]) return same;
            Vector2 at = ParseV(p[2]);
            return DungeonMobs().Where(w => w.obj_id == p[1]).OrderBy(w => Vector2.Distance(w.pos, at)).FirstOrDefault();
        }

        /// <summary>Both players in a dungeon level see the same monsters; each kills one and
        /// the other sees it die (and stay dead); then the host leads both out.</summary>
        private static IEnumerator DungeonCombat()
        {
            Bot.KeepAlive = true;   // off only for the planned death below; Suite resets it
            var me = MainGame.me.player;
            if (Bot.IsHost)
            {
                MainGame.me.TeleportToDungeonLevel(1);
                yield return Bot.WaitUntil("in the dungeon", InDungeon, 15f);
                yield return Bot.Wait(4f);
                Bot.Publish("dmobs_in", "1");
            }
            else
            {
                yield return Bot.WaitUntil("host in the dungeon", () => Bot.Remote(1, "dmobs_in") != null, 60f);
                yield return Bot.WaitUntil("taken along into the dungeon", InDungeon, 20f);
                yield return Bot.Wait(4f);
            }
            var mobs = DungeonMobs();
            Bot.Step($"info {mobs.Count} monsters here: {MobList(mobs)}");
            {
                var all = MainGame.me.dungeon_root.GetComponentsInChildren<WorldGameObject>(true)
                    .Where(w => w != null && w.obj_def != null && w.obj_def.type == ObjectDefinition.ObjType.Mob).ToList();
                Bot.Step($"info mob objects under the dungeon: {all.Count}; " + string.Join(",", all.Where(w => !mobs.Contains(w))
                    .Select(w => $"{w.obj_id}#{w.unique_id % 1000000} hp={F(w.hp)} removed={w.is_removed} active={w.gameObject.activeInHierarchy} listed={WorldMap.objs.Contains(w)} dead={w.is_dead}")));
                // Live objects with a dungeon id that are not part of the current level (left over from an earlier copy).
                var reg = GraveyardKeeperCoop.Multiplayer.WGORegistry.Instance?.SnapshotAll() ?? new List<WorldGameObject>();
                var stale = reg.Concat(WorldMap.objs).Where(w => w != null && w.unique_id >= 2000000000000L &&
                                                                 !w.transform.IsChildOf(MainGame.me.dungeon_root.transform)).Distinct().ToList();
                Bot.Step($"info stale dungeon-id objects: {stale.Count}; " + string.Join(",", stale.Take(10)
                    .Select(w => $"{w.obj_id}#{w.unique_id % 1000000} parent={(w.transform.parent ? w.transform.parent.name : "-")} active={w.gameObject.activeInHierarchy}")));
            }
            Bot.Publish("dmobs", MobKinds(mobs));
            yield return Bot.WaitUntil("partner's monsters", () => Bot.Remote(Other, "dmobs") != null, 30f);
            Bot.Check("the same monsters (kind and id) on both sides", MobKinds(mobs) == Bot.Remote(Other, "dmobs"),
                      $"here [{MobKinds(mobs)}] there [{Bot.Remote(Other, "dmobs")}]");

            // The client kills one first, then the host kills another.
            foreach (int fighter in new[] { 2, 1 })
            {
                string key = "dkill" + fighter;
                if (Plugin.Player == fighter)
                {
                    var target = DungeonMobs().OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                    Bot.Check("a monster to fight", target != null);
                    if (target == null) { Bot.Publish(key, "none"); continue; }
                    string ann = $"{target.unique_id}|{target.obj_id}|{V(target.pos)}";
                    Bot.Publish(key, ann);
                    yield return Bot.WaitUntil("partner found it", () => Bot.Remote(Other, key + "_found") != null, 20f);
                    yield return Fight(target.unique_id, target.obj_id, 60f);
                    yield return StaysGone(target.unique_id, target.obj_id, 4f);
                    Bot.Publish(key + "_done", "1");
                    yield return Bot.WaitUntil("partner saw it die", () => Bot.Remote(Other, key + "_seen") != null, 40f);
                }
                else
                {
                    yield return Bot.WaitUntil("partner picked a monster", () => Bot.Remote(fighter, key) != null, 30f);
                    string ann = Bot.Remote(fighter, key);
                    if (ann == "none") continue;
                    var m = MatchMob(ann);
                    Bot.Check("the partner's monster exists here", m != null, ann + " | here: " + MobList(DungeonMobs()));
                    Bot.Step($"info partner fights {ann}; here {(m == null ? "none" : $"{m.obj_id}#{m.unique_id}@{V(m.pos)} hp={F(m.hp)}")}");
                    Bot.Publish(key + "_found", "1");
                    if (m == null) { Bot.Publish(key + "_seen", "1"); continue; }
                    long uid = m.unique_id; string id = m.obj_id; float hp0 = m.hp, minHp = hp0;
                    float t0 = UnityEngine.Time.realtimeSinceStartup;
                    while (Bot.Remote(fighter, key + "_done") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 75f)
                    {
                        var w = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
                        if (w != null && !w.is_removed) minHp = Mathf.Min(minHp, w.hp);
                        // Standing idle among monsters: do not let them kill the waiting player (a death
                        // takes the player home and reloads the level, which is a different test).
                        if (MainGame.me.player.hp < 60f) MainGame.me.player.hp = 100f;
                        yield return null;
                    }
                    Bot.Step($"info monster hp here: {F(hp0)} -> min {F(minHp)}; now {Describe(uid)}");
                    Bot.Check("saw the monster take damage", minHp < hp0 || Gone(uid, id));
                    yield return Bot.WaitUntil("the monster died here too", () => Gone(uid, id), 10f);
                    yield return StaysGone(uid, id, 4f);
                    Bot.Publish(key + "_seen", "1");
                }
            }

            mobs = DungeonMobs();
            Bot.Publish("dmobs_after", MobKinds(mobs));
            yield return Bot.WaitUntil("partner's monsters after", () => Bot.Remote(Other, "dmobs_after") != null, 30f);
            yield return Bot.Wait(1f);
            string mine = MobKinds(DungeonMobs());
            Bot.Check("the same monsters left on both sides", mine == Bot.Remote(Other, "dmobs_after"),
                      $"here [{mine}] there [{Bot.Remote(Other, "dmobs_after")}]");
            if (mine != Bot.Remote(Other, "dmobs_after")) Bot.Step("info here: " + MobList(DungeonMobs()));

            // The partner dies in the dungeon: the game takes them home and unloads the level, the
            // coop mod loads the host's level again. The monsters killed before must stay dead.
            if (Bot.IsHost)
            {
                // Idle among the monsters while the partner dies and comes back: keep the host alive.
                yield return Bot.WaitUntil("partner back after dying", () =>
                {
                    if (MainGame.me.player.hp < 60f) MainGame.me.player.hp = 100f;
                    return Bot.Remote(2, "ddeath_back") == "1";
                }, 120f);
                Bot.Publish("dmobs_after2", MobKinds(DungeonMobs()));
            }
            else
            {
                // Not the first death: that one plays a scripted scene ("alive and back home") that
                // holds the player for a while and is not what this checks.
                if (MainGame.me.player.GetParamInt("deaths_count") < 3) MainGame.me.player.SetParam("deaths_count", 5f);
                Bot.KeepAlive = false;
                MainGame.me.player.hp = 0f;
                MainGame.me.player.DoZeroHPActivity();
                yield return Bot.WaitUntil("death message shown", () => GUIElements.me.dialog != null && GUIElements.me.dialog.is_shown, 10f);
                yield return Bot.Wait(3f);
                yield return FakeInput.Press(GameKey.Select);
                yield return Bot.Wait(0.5f);
                if (GUIElements.me.dialog.is_shown) { Bot.Step("info closing the death message directly"); GUIElements.me.dialog.Hide(); }
                yield return Bot.WaitUntil("can move after dying", CanMove, 20f);
                Bot.KeepAlive = true;
                yield return Bot.WaitUntil("host's level loaded again", InDungeon, 30f);
                yield return Bot.Wait(2f);
                Bot.Publish("ddeath_back", "1");
                yield return Bot.WaitUntil("host's monsters", () => Bot.Remote(1, "dmobs_after2") != null, 30f);
                string now = MobKinds(DungeonMobs());
                Bot.Check("after dying: the same monsters as the host", now == Bot.Remote(1, "dmobs_after2"),
                          $"here [{now}] there [{Bot.Remote(1, "dmobs_after2")}]");
            }

            if (Bot.IsHost)
            {
                yield return Bot.WaitRemote(2, "dmobs_ready_out", "1", 30f);
                var exit = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id == "dungeon_exit")
                                        .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("found the dungeon exit", exit != null);
                if (exit != null)
                {
                    yield return WalkUpTo(exit, 120f);
                    yield return FakeInput.Press(GameKey.Interaction);
                    yield return Bot.WaitUntil("left the dungeon", () => !InDungeon(), 20f);
                }
                yield return Bot.WaitUntil("can move again", CanMove, 10f);
                Bot.Publish("dmobs_out", "1");
                yield return Bot.WaitRemote(2, "dmobs_done", "1", 60f);
            }
            else
            {
                Bot.Publish("dmobs_ready_out", "1");
                yield return Bot.WaitUntil("host walked out", () => Bot.Remote(1, "dmobs_out") != null, 90f);
                yield return Bot.WaitUntil("taken out of the dungeon too", () => !InDungeon(), 20f);
                yield return Bot.WaitUntil("can move again", CanMove, 10f);
                Bot.Publish("dmobs_done", "1");
            }
        }

        private static int DungeonLevel() =>
            InDungeon() ? (MainGame.me.dungeon_root.cur_dungeon_preset?.dungeon_level ?? -1) : 0;

        /// <summary>Both sides list their monsters under <paramref name="key"/>; they must match.</summary>
        private static IEnumerator SameMobs(string key, string what)
        {
            Bot.Publish(key, MobKinds(DungeonMobs()));
            yield return Bot.WaitUntil("partner's monsters " + key, () => Bot.Remote(Other, key) != null, 40f);
            yield return Bot.Wait(1f);
            string mine = MobKinds(DungeonMobs()), there = Bot.Remote(Other, key);
            Bot.Check(what, mine == there, $"here [{mine}] there [{there}]");
            if (mine != there) Bot.Step("info here: " + MobList(DungeonMobs()));
        }

        /// <summary>One player kills the nearest monster, the other sees it die. Returns "uid|kind" in <paramref name="killed"/>.</summary>
        private static IEnumerator KillOne(int fighter, string key, string[] killed)
        {
            var me = MainGame.me.player;
            if (Plugin.Player == fighter)
            {
                var target = DungeonMobs().OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("a monster to fight", target != null);
                string ann = target == null ? "none" : $"{target.unique_id}|{target.obj_id}|{V(target.pos)}";
                Bot.Publish(key, ann);
                if (target == null) yield break;
                killed[0] = ann;
                yield return Fight(target.unique_id, target.obj_id, 60f);
                Bot.Publish(key + "_done", "1");
                yield return Bot.WaitUntil("partner saw it die", () => Bot.Remote(Other, key + "_seen") != null, 40f);
            }
            else
            {
                yield return Bot.WaitUntil("partner picked a monster", () => Bot.Remote(fighter, key) != null, 30f);
                string ann = Bot.Remote(fighter, key);
                if (ann == "none") yield break;
                killed[0] = ann;
                var p = ann.Split('|');
                long uid = long.Parse(p[0]);
                Bot.Check("the partner's monster exists here", !Gone(uid, p[1]), ann + " | here: " + MobList(DungeonMobs()));
                yield return Bot.WaitUntil("partner finished the fight", () => Bot.Remote(fighter, key + "_done") != null, 75f);
                yield return Bot.WaitUntil("the monster died here too", () => Gone(uid, p[1]), 10f);
                Bot.Publish(key + "_seen", "1");
            }
        }

        private static void StillDead(string ann, string where)
        {
            if (string.IsNullOrEmpty(ann)) return;
            var p = ann.Split('|');
            Bot.Check($"the {p[1]} killed on {where} is still dead", Gone(long.Parse(p[0]), p[1]), Describe(long.Parse(p[0])));
        }

        /// <summary>The host leads both between dungeon levels 1 and 2: each level looks the same on
        /// both sides, kills stay on the level they were made on, and the client cannot wander off to
        /// another level alone (it follows the host).</summary>
        private static IEnumerator DungeonLevels()
        {
            Bot.KeepAlive = true;
            string[] kill1 = new string[1], kill2 = new string[1];

            yield return GoToLevel(1, "dl_in1");
            yield return SameMobs("dl_m1", "level 1: the same monsters on both sides");
            yield return KillOne(1, "dl_k1", kill1);
            yield return Bot.Wait(1f);
            int left1 = DungeonMobs().Count;

            // The client tries another level on its own and is brought back to the host's.
            if (Bot.IsHost)
            {
                yield return Bot.WaitUntil("partner tried a level alone", () => Bot.Remote(2, "dl_solo") != null, 90f);
                Bot.Check("the host stayed on level 1", DungeonLevel() == 1, "level " + DungeonLevel());
            }
            else
            {
                MainGame.me.TeleportToDungeonLevel(2);
                yield return Bot.Wait(1f);
                Bot.Step("info alone on level " + DungeonLevel());
                yield return Bot.WaitUntil("brought back to the host's level 1", () => DungeonLevel() == 1, 40f);
                yield return Bot.WaitUntil("can move", CanMove, 15f);
                yield return Bot.Wait(2f);
                StillDead(kill1[0], "level 1");
                Bot.Publish("dl_solo", "1");
            }
            yield return SameMobs("dl_m1b", "level 1 after the solo trip: the same monsters");

            yield return GoToLevel(2, "dl_in2");
            yield return SameMobs("dl_m2", "level 2: the same monsters on both sides");
            yield return KillOne(2, "dl_k2", kill2);
            yield return Bot.Wait(1f);
            int left2 = DungeonMobs().Count;

            // A revisited level is rebuilt from the host's save, which gives the monsters left new
            // ids (as in the single-player game); what must hold is that the dead stay dead.
            yield return GoToLevel(1, "dl_in1c");
            Bot.Check("back on level 1: the killed monster did not come back", DungeonMobs().Count == left1, $"{DungeonMobs().Count} monsters, {left1} were left");
            yield return SameMobs("dl_m1c", "back on level 1: the same monsters");

            yield return GoToLevel(2, "dl_in2c");
            Bot.Check("back on level 2: the killed monster did not come back", DungeonMobs().Count == left2, $"{DungeonMobs().Count} monsters, {left2} were left");
            yield return SameMobs("dl_m2c", "back on level 2: the same monsters");

            // Out of the dungeon.
            var me = MainGame.me.player;
            if (Bot.IsHost)
            {
                yield return Bot.WaitRemote(2, "dl_ready_out", "1", 30f);
                var exit = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id == "dungeon_exit")
                                        .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("found the dungeon exit", exit != null);
                if (exit != null)
                {
                    yield return WalkUpTo(exit, 120f);
                    yield return FakeInput.Press(GameKey.Interaction);
                    yield return Bot.WaitUntil("left the dungeon", () => !InDungeon(), 20f);
                }
                yield return Bot.WaitUntil("can move again", CanMove, 10f);
                Bot.Publish("dl_out", "1");
                yield return Bot.WaitRemote(2, "dl_done", "1", 60f);
            }
            else
            {
                Bot.Publish("dl_ready_out", "1");
                yield return Bot.WaitUntil("host walked out", () => Bot.Remote(1, "dl_out") != null, 90f);
                yield return Bot.WaitUntil("taken out of the dungeon too", () => !InDungeon(), 20f);
                yield return Bot.WaitUntil("can move again", CanMove, 10f);
                Bot.Publish("dl_done", "1");
            }
        }

        private static IEnumerator GoToLevel(int level, string key)
        {
            if (Bot.IsHost)
            {
                MainGame.me.TeleportToDungeonLevel(level);
                yield return Bot.WaitUntil("on level " + level, () => DungeonLevel() == level, 15f);
                yield return Bot.WaitUntil("can move", CanMove, 15f);
                yield return Bot.Wait(4f);
                Bot.Publish(key, "1");
                yield return Bot.WaitRemote(2, key + "_ok", "1", 60f);
            }
            else
            {
                yield return Bot.WaitUntil("host on level " + level, () => Bot.Remote(1, key) != null, 60f);
                yield return Bot.WaitUntil("taken along to level " + level, () => DungeonLevel() == level, 30f);
                yield return Bot.WaitUntil("can move", CanMove, 15f);
                yield return Bot.Wait(4f);
                Bot.Publish(key + "_ok", "1");
            }
        }

        // ------------------------------------------------------------------ building
        private static readonly System.Reflection.FieldInfo GamepadActiveF = HarmonyLib.AccessTools.Field(typeof(LazyInput), "_gamepad_active");
        private static readonly System.Reflection.FieldInfo BuildModeF = HarmonyLib.AccessTools.Field(typeof(BuildModeLogics), "_mode");

        private static string BuildMode() => BuildModeF.GetValue(MainGame.me.build_mode_logics)?.ToString();

        private static List<WorldGameObject> BuiltNear(string outObj, Vector2 at, float r = 120f) =>
            WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null &&
                                     (w.obj_id == outObj || w.obj_id == outObj + "_place") &&
                                     Vector2.Distance(w.pos, at) < r).ToList();

        /// <summary>Build like a player: open the build desk, pick a recipe, place it in the zone.
        /// The partner sees the new object at the same spot; the materials leave only the builder's bag.</summary>
        private static IEnumerator Build(int builder)
        {
            var me = MainGame.me.player;
            if (Plugin.Player == builder)
            {
                Func<WorldGameObject, List<ObjectCraftDefinition>> putCrafts = d =>
                    GameBalance.me.craft_obj_data.Where(c => c != null && c.builder_ids.Contains(d.obj_id) &&
                        c.build_type == ObjectCraftDefinition.BuildType.Put && MainGame.me.save.IsCraftVisible(c) &&
                        string.IsNullOrEmpty(c.sub_zone_id) && string.IsNullOrEmpty(c.end_script) && !c.one_time_craft &&
                        c.needs.All(n => GameBalance.me.GetDataOrNull<ItemDefinition>(n.id) != null)).ToList();
                var desks = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_def != null &&
                                                    !string.IsNullOrEmpty(w.obj_def.zone_id) && putCrafts(w).Count > 0)
                                         .OrderBy(w => (w.pos - me.pos).sqrMagnitude).ToList();
                Bot.Step("info build desks: " + string.Join(",", desks.Take(6).Select(d => $"{d.obj_id}@{d.obj_def.zone_id}({putCrafts(d).Count})")));
                // Each builder starts at its own desk (the host's build can fill the only free spot);
                // a recipe with no free spot in its zone is cancelled and the next one tried.
                var order = desks.Skip(builder - 1).Concat(desks.Take(builder - 1)).ToList();
                var candidates = order.SelectMany(d => putCrafts(d).OrderBy(c => c.needs.Sum(n => n.value)).Take(2).Select(c => new { d, c })).Take(6).ToList();
                Bot.Check("found a build desk", candidates.Count > 0);
                if (candidates.Count == 0) { Bot.Publish("built", "none"); yield break; }
                WorldGameObject desk = null; ObjectCraftDefinition cd = null;
                string bag0 = null;
                Vector2 spot = Vector2.zero; bool free = false;
                object wasGamepad = GamepadActiveF.GetValue(null);
                foreach (var cand in candidates)
                {
                    desk = cand.d; cd = cand.c;
                    Bot.Step($"info building {cd.id} -> {cd.out_obj}, needs {Items(cd.needs)} at {desk.obj_id} (zone {desk.obj_def.zone_id})");
                    foreach (var n in cd.needs)
                        me.data.AddItem(n.id, n.value);
                    bag0 = Items(me.data.inventory);

                    yield return WalkUpTo(desk);
                    MainGame.me.OpenBuildObjectGUI(desk);       // E on the build desk
                    yield return Bot.Wait(1f);
                    Bot.Check("build menu open", GUIElements.me.craft.is_shown);
                    GamepadActiveF.SetValue(null, true);         // move the blueprint with keys, not the mouse
                    try { MainGame.me.build_mode_logics.CraftBuilding(cd); }   // click on the recipe
                    catch (Exception e) { Bot.Step("FAIL picking the recipe: " + e.Message); }
                    yield return Bot.Wait(0.5f);
                    Bot.Check("placing a blueprint", BuildMode() == "Placing" && FloatingWorldGameObject.cur_floating != null, BuildMode());

                    // Find a free spot in the zone, near the desk.
                    var b = WorldZone.GetZoneByID(desk.obj_def.zone_id).GetBounds();
                    var tries = new List<Vector2>();
                    for (float x = b.min.x + 96f; x < b.max.x - 96f; x += 96f)
                        for (float y = b.min.y + 96f; y < b.max.y - 96f; y += 96f)
                            tries.Add(new Vector2(x, y));
                    int freeCells = 0;
                    bool canAfford = MainGame.me.build_mode_logics.CanBuild(cd);
                    foreach (var t in tries.OrderBy(t => (t - (Vector2)desk.pos).sqrMagnitude).Take(400))
                    {
                        if (FloatingWorldGameObject.cur_floating == null) break;
                        FloatingWorldGameObject.MoveCurrentFloatingObject(t);
                        yield return null;
                        if (FloatingWorldGameObject.can_be_built) freeCells++;
                        if (FloatingWorldGameObject.can_be_built && MainGame.me.build_mode_logics.CanBuild(cd)) { spot = t; free = true; break; }
                    }
                    Bot.Step($"info {tries.Count} spots in the zone; free {freeCells}; can afford {canAfford}");
                    if (free) break;
                    // Back out like a player (Esc while placing, then close the menu) and give the materials back.
                    yield return FakeInput.Press(GameKey.Back);
                    yield return Bot.Wait(0.6f);
                    if (GUIElements.me.craft.is_shown) { GUIElements.me.craft.OnClosePressed(); yield return Bot.Wait(0.6f); }
                    foreach (var n in cd.needs)
                        me.data.RemoveItem(n.id, n.value);
                }
                Bot.Check("found a free spot in the zone", free);
                var preview = FloatingWorldGameObject.cur_floating?.wobj;
                Vector2 placedAt = preview != null ? (Vector2)preview.pos : spot;
                var before = new HashSet<WorldGameObject>(BuiltNear(cd.out_obj, placedAt, 400f).Where(w => w != preview));
                if (free)
                {
                    yield return FakeInput.Press(GameKey.Interaction);   // place it
                    yield return Bot.Wait(1f);
                }
                var built = BuiltNear(cd.out_obj, placedAt).FirstOrDefault(w => !before.Contains(w) && w != FloatingWorldGameObject.cur_floating?.wobj);
                Bot.Check("blueprint placed", built != null, built == null ? $"nothing at {V(placedAt)}" : $"{built.obj_id}#{built.unique_id} at {V(built.pos)}");
                if (cd.needs.Count > 0)
                    Bot.Check("materials used", Items(me.data.inventory) != bag0, $"[{bag0}] -> [{Items(me.data.inventory)}]");

                // Leave build mode: Back out of placing, then close the build menu.
                for (int i = 0; i < 4 && (BuildMode() == "Placing" || GUIElements.me.craft.is_shown || MainGame.me.game_mode == MainGame.GameMode.Building); i++)
                {
                    yield return FakeInput.Press(GameKey.Back);
                    yield return Bot.Wait(0.6f);
                    if (GUIElements.me.craft.is_shown) { GUIElements.me.craft.OnClosePressed(); yield return Bot.Wait(0.6f); }
                }
                GamepadActiveF.SetValue(null, wasGamepad);
                Bot.Check("left build mode", MainGame.me.game_mode != MainGame.GameMode.Building && !GUIElements.me.craft.is_shown,
                          $"mode={MainGame.me.game_mode} build={BuildMode()} menu={GUIElements.me.craft.is_shown}");
                yield return Bot.WaitUntil("can move again", CanMove, 5f);
                Bot.Publish("built", built == null ? "none" : $"{built.obj_id}|{F(built.pos.x)}|{F(built.pos.y)}|{cd.out_obj}");
                yield return Bot.WaitRemote(Other, "build_seen", "1", 40f);
            }
            else
            {
                string bag0 = Items(me.data.inventory);
                yield return Bot.WaitUntil("builder done", () => Bot.Remote(builder, "built") != null, 120f);
                string r = Bot.Remote(builder, "built");
                if (r != "none")
                {
                    var p = r.Split('|');
                    var at = new Vector2(PF(p[1]), PF(p[2]));
                    yield return Bot.WaitUntil($"I see the {p[0]} at the same spot", () => BuiltNear(p[3], at, 48f).Any(w => w.obj_id == p[0]), 20f);
                    var mine = BuiltNear(p[3], at, 400f);
                    Bot.Check("exactly one copy here", mine.Count(w => Vector2.Distance(w.pos, at) < 48f) == 1, string.Join(",", mine.Select(w => $"{w.obj_id}@{V(w.pos)}")));
                    Bot.Check("my bag unchanged", Items(me.data.inventory) == bag0, $"[{bag0}] -> [{Items(me.data.inventory)}]");
                }
                Bot.Publish("build_seen", "1");
            }
        }

        // ------------------------------------------------------------------ fishing
        private static readonly System.Reflection.FieldInfo FishingStateF = HarmonyLib.AccessTools.Field(typeof(FishingGUI), "_state");
        private static readonly System.Reflection.MethodInfo FishingChangeState = HarmonyLib.AccessTools.Method(typeof(FishingGUI), "ChangeState");

        private static FishingGUI.FishingState FishState() =>
            GUIElements.me.fishing == null ? FishingGUI.FishingState.None : (FishingGUI.FishingState)FishingStateF.GetValue(GUIElements.me.fishing);

        private static int RemoteFishers()
        {
            var sync = GraveyardKeeperCoop.Multiplayer.FishingPresentationSync.Instance;
            var remotes = sync == null ? null : HarmonyLib.AccessTools.Field(sync.GetType(), "remotes").GetValue(sync) as System.Collections.IDictionary;
            return remotes?.Count ?? 0;
        }

        /// <summary>Fish like a player: pick the bait (E), hold E to cast, press the minigame key
        /// on a bite. Only the reel-in minigame (a skill test) is decided through the game's state.
        /// The partner sees the fishing, the fish lands only in the fisher's bag, nobody is locked.</summary>
        private static IEnumerator Fishing(int fisher)
        {
            var me = MainGame.me.player;
            string partnerBag0 = Items(me.data.inventory);
            if (Plugin.Player == fisher)
            {
                var spot = WorldMap.objs.Where(w => w != null && w.obj_id != null && GameBalance.me.GetDataOrNull<ReservoirsDefinition>(w.obj_id) != null)
                                        .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("found a fishing spot", spot != null);
                if (spot == null) { Bot.Publish("fish", "none"); yield break; }
                if (me.GetEquippedItem(ItemDefinition.EquipmentType.FishingRod) == null)
                {
                    var rodDef = GameBalance.me.items_data.FirstOrDefault(i => i != null && i.equipment_type == ItemDefinition.EquipmentType.FishingRod);
                    me.data.AddItem(rodDef.id, 1);
                    me.EquipItem(me.data.inventory.First(i => i != null && i.id == rodDef.id));
                }
                // A plain bait (not a fish used as bait for big fish).
                foreach (var b in new[] { "maggot", "moth", "butterfly" })
                    if (GameBalance.me.GetDataOrNull<ItemDefinition>(b) != null && me.data.GetItemsCount(b) == 0)
                        me.data.AddItem(b, 3);
                // What bites here right now, by the game's fish tables: a bait and a distance with a catch.
                int rodLvl = int.Parse(me.GetEquippedItem(ItemDefinition.EquipmentType.FishingRod).id.Substring("fishing_rod_".Length));
                var reservoir = GameBalance.me.GetData<ReservoirsDefinition>(spot.obj_id);
                string wantBait = null; int wantDist = 0;
                foreach (var b in new[] { "maggot", "moth", "butterfly", "" })
                {
                    for (int dist = 1; dist <= 3 && wantDist == 0; dist++)
                        if (reservoir.dist_avaliables[dist - 1] &&
                            GameBalance.me.fishes_data.Any(f => f.GetTotalWeight(spot.obj_id, TimeOfDay.me.is_night, dist, rodLvl, b) > 0.01f))
                        { wantBait = b; wantDist = dist; }
                    if (wantDist != 0) break;
                }
                Bot.Check("something bites here now", wantDist != 0, $"bait '{wantBait}' at distance {wantDist}, night={TimeOfDay.me.is_night}");
                if (wantDist == 0) { Bot.Publish("fish", "none"); yield break; }
                string rod = me.GetEquippedItem(ItemDefinition.EquipmentType.FishingRod)?.id;
                int baits0 = me.data.inventory.Where(i => i != null && i.definition?.type == ItemDefinition.ItemType.Bait).Sum(i => i.value);
                var gd = spot.GetComponentInChildren<GDPoint>();
                me.PlaceAtPos(gd != null ? (Vector2)gd.transform.position : spot.pos);
                yield return Bot.Wait(1f);
                Bot.Step($"info fishing at {spot.obj_id}#{spot.unique_id} with {rod}, baits {baits0}");
                Bot.Publish("fish", $"{spot.unique_id}");

                GUIElements.me.fishing.Open(spot);   // what E on the fishing spot does
                yield return Bot.WaitUntil("choosing the bait", () => FishState() == FishingGUI.FishingState.BaitChoosing, 5f);
                yield return Bot.Wait(0.5f);
                // Pick a bait with the tab keys, like a player.
                var fgui = GUIElements.me.fishing;
                var curBait = HarmonyLib.AccessTools.Field(typeof(FishingGUI), "_cur_bait_num");
                var baitsList = HarmonyLib.AccessTools.Field(typeof(FishingGUI), "_avaliable_baits");
                Func<string> chosen = () =>
                {
                    int n = (int)curBait.GetValue(fgui);
                    return n >= 0 ? ((List<Item>)baitsList.GetValue(fgui))[n]?.id : null;
                };
                for (int i = 0; i < 8 && (chosen() ?? "") != wantBait; i++)
                {
                    yield return FakeInput.Press(GameKey.NextTab);
                    yield return Bot.Wait(0.3f);
                }
                int bi = (int)curBait.GetValue(fgui);
                string baitId = bi >= 0 ? ((List<Item>)baitsList.GetValue(fgui))[bi]?.id : null;
                Bot.Check("chose the bait", (baitId ?? "") == wantBait, baitId ?? "no bait");
                int bait0 = baitId == null ? 0 : me.data.GetItemsCount(baitId);
                // Hold E: the press leaves the bait choice, holding fills the distance bar, letting go casts.
                // (The game ignores E until the key that opened fishing is released: retry if so.)
                for (int i = 0; i < 4 && FishState() == FishingGUI.FishingState.BaitChoosing; i++)
                {
                    // The bar fills in 2 s; distance n is the n-th third of it.
                    yield return FakeInput.Hold(GameKey.Interaction, wantDist == 1 ? 0.35f : wantDist == 2 ? 1.0f : 1.7f);
                    yield return Bot.Wait(0.5f);
                }
                Bot.Step($"info after the cast: state={FishState()} energy={me.energy:F1}");
                yield return Bot.WaitUntil("cast, waiting for a bite", () => FishState() == FishingGUI.FishingState.WaitingForBite ||
                                                                           FishState() == FishingGUI.FishingState.WaitingForPulling, 5f);
                Bot.Publish("fish_cast", "1");
                yield return Bot.WaitUntil("a fish bites", () => FishState() == FishingGUI.FishingState.WaitingForPulling, 40f);
                yield return FakeInput.Press(GameKey.MiniGameAction);
                yield return Bot.WaitUntil("reeling in", () => FishState() == FishingGUI.FishingState.Pulling, 5f);
                yield return Bot.Wait(2f);
                var gui = GUIElements.me.fishing;
                var fish = HarmonyLib.AccessTools.Field(typeof(FishingGUI), "_fish").GetValue(gui) as Item;
                string fishId = fish?.id;
                int fish0 = fishId == null ? 0 : me.data.GetItemsCount(fishId);
                // The reel-in skill game: decide it as won.
                if (FishState() == FishingGUI.FishingState.Pulling)
                {
                    HarmonyLib.AccessTools.Field(typeof(FishingGUI), "is_success_fishing").SetValue(gui, true);
                    FishingChangeState.Invoke(gui, new object[] { FishingGUI.FishingState.TakingOut });
                }
                else if (gui.is_shown) gui.Hide();
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (FishState() != FishingGUI.FishingState.None && UnityEngine.Time.realtimeSinceStartup - t0 < 6f) yield return null;
                if (FishState() != FishingGUI.FishingState.None) { gui.taking_out_animation_finished = true; yield return Bot.Wait(0.5f); }
                Bot.Check("fishing finished", FishState() == FishingGUI.FishingState.None && !gui.is_shown, $"state={FishState()}");
                int fish1 = fishId == null ? 0 : me.data.GetItemsCount(fishId);
                Bot.Check("caught a fish", fishId != null && fish1 == fish0 + 1, $"{fishId}: {fish0} -> {fish1}");
                // A caught fish can itself be a bait: discount it.
                int bait1 = baitId == null ? 0 : me.data.GetItemsCount(baitId) - (baitId == fishId ? 1 : 0);
                if (baitId != null)
                    Bot.Check("used one bait", bait1 == bait0 - 1, $"{baitId}: {bait0} -> {bait1}");
                yield return Bot.WaitUntil("can move again", CanMove, 5f);
                Bot.Publish("fish_done", fishId ?? "none");
                Bot.Publish("known_fish_" + Plugin.Player, MainGame.me.save.known_fishes.Count.ToString());
                yield return Bot.WaitRemote(Other, "fish_seen", "1", 30f);
            }
            else
            {
                yield return Bot.WaitUntil("fisher found a spot", () => Bot.Remote(fisher, "fish") != null, 30f);
                if (Bot.Remote(fisher, "fish") == "none") yield break;
                yield return Bot.WaitUntil("fisher cast", () => Bot.Remote(fisher, "fish_cast") != null, 30f);
                yield return Bot.WaitUntil("partner is shown fishing here", () => RemoteFishers() > 0, 10f);
                Bot.Check("I can still move while the partner fishes", CanMove());
                yield return Bot.WaitUntil("fisher done", () => Bot.Remote(fisher, "fish_done") != null, 90f);
                yield return Bot.WaitUntil("partner no longer shown fishing", () => RemoteFishers() == 0, 10f);
                string fishId = Bot.Remote(fisher, "fish_done");
                Bot.Check("the fish did not land in my bag", Items(me.data.inventory) == partnerBag0, $"before [{partnerBag0}] now [{Items(me.data.inventory)}]");
                Bot.Step($"info known fishes here {MainGame.me.save.known_fishes.Count}, fisher {Bot.Remote(fisher, "known_fish_" + fisher)}");
                Bot.Publish("fish_seen", "1");
            }
        }

        // ------------------------------------------------------------------ morgue and graves
        private static IEnumerator MorgueProbe()
        {
            if (!Bot.IsHost) yield break;
            var me = MainGame.me.player;
            Func<WorldGameObject, string> d = w => $"{w.obj_id}#{w.unique_id}@{F((w.pos - me.pos).magnitude / 96f)}t";
            var graves = WorldMap.objs.Where(w => w?.obj_def != null && w.obj_def.interaction_type == ObjectDefinition.InteractionType.Grave).ToList();
            Bot.Step($"info graves ({graves.Count}): " + string.Join(",", graves.GroupBy(g => g.obj_id).Select(g => g.Key + "x" + g.Count())));
            foreach (var g in graves.OrderBy(g => (g.pos - me.pos).sqrMagnitude).Take(4))
                Bot.Step($"     grave {d(g)} inv=[{Items(g.data.inventory)}] crafts=[{string.Join(",", g.components?.craft?.crafts?.Take(6).Select(c => c.id) ?? new string[0])}]");
            foreach (var w in WorldMap.objs.Where(w => w != null && w.obj_id != null &&
                     (w.is_autopsy_table || w.obj_id.Contains("morgue") || w.obj_id.Contains("body") || w.obj_id.Contains("corpse") || w.obj_id.Contains("grave_place") || w.obj_id.Contains("empty_grave"))).Take(20))
                Bot.Step($"     obj {d(w)} autopsy={w.is_autopsy_table} type={w.obj_def?.interaction_type} inv=[{Items(w.data.inventory)}] crafts=[{string.Join(",", w.components?.craft?.crafts?.Take(8).Select(c => c.id) ?? new string[0])}]");
            var bodyItems = GameBalance.me.items_data.Where(i => i != null && i.type == ItemDefinition.ItemType.Body).Select(i => i.id).Take(20);
            Bot.Step("info body item ids: " + string.Join(",", bodyItems));
            Bot.Step($"info overhead={MainGame.me.player_char.GetOverheadItem()?.id}");
            yield break;
        }

        // ------------------------------------------------------------------ gathering and combat
        private static readonly System.Reflection.MethodInfo OnChangeDirM =
            HarmonyLib.AccessTools.Method(typeof(BaseCharacterComponent), "OnChangeDir");

        private static void Dir(Vector2 d) => OnChangeDirM.Invoke(MainGame.me.player_char, new object[] { d });

        /// <summary>Teleport near the target, then walk into it from each side (keyboard
        /// walking) until the game highlights it as the object in front of the player.</summary>
        private static IEnumerator WalkUpTo(WorldGameObject target, float from = 200f)
        {
            var me = MainGame.me.player;
            var inter = me.components.interaction;
            foreach (var dir in new[] { Vector2.up, Vector2.right, Vector2.left, Vector2.down })
            {
                if (target == null || target.is_removed) yield break;
                me.PlaceAtPos(target.pos - dir * from);
                yield return Bot.Wait(0.4f);
                float w0 = UnityEngine.Time.realtimeSinceStartup;
                while (inter.nearest != target && UnityEngine.Time.realtimeSinceStartup - w0 < 2.5f)
                {
                    Dir(dir);
                    yield return null;
                }
                Dir(Vector2.zero);
                yield return Bot.Wait(0.3f);
                if (inter.nearest == target) yield break;
            }
        }

        private static bool Gone(long uid, string objId)
        {
            var w = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
            return w == null || w.is_removed || w.obj_id != objId || w.hp <= 0f;
        }

        /// <summary>A destroyed object must not come back (e.g. restored by a stale state update).</summary>
        private static IEnumerator StaysGone(long uid, string objId, float seconds)
        {
            float t0 = UnityEngine.Time.realtimeSinceStartup;
            string back = null;
            while (UnityEngine.Time.realtimeSinceStartup - t0 < seconds)
            {
                if (!Gone(uid, objId)) back = Describe(uid);
                yield return null;
            }
            Bot.Check($"{objId} stays destroyed", back == null, back == null ? "" : "came back as " + back);
        }

        private static int DropsNear(Vector2 at, float r = 350f) =>
            UnityEngine.Object.FindObjectsOfType<DropResGameObject>()
                .Count(d => d != null && !d.is_collected && d.res != null && Vector2.Distance(d.transform.position, at) < r);

        private static string Describe(long uid)
        {
            var w = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
            return w == null ? "none" : $"{w.obj_id} hp={F(w.hp)} removed={w.is_removed}";
        }

        /// <summary>One player chops a tree or breaks a stone with the work key; the other
        /// sees its hp drop, sees it destroyed and sees the same resources on the ground.</summary>
        private static IEnumerator Gather(int gatherer)
        {
            if (Plugin.Player == gatherer)
            {
                var me = MainGame.me.player;
                var target = WorldMap.objs
                    .Where(w => w != null && !w.is_player && !w.is_removed && w.unique_id > 0 && w.obj_def != null &&
                                w.obj_def.type == ObjectDefinition.ObjType.Default && w.hp > 0f &&
                                (w.obj_id.StartsWith("tree") || w.obj_id.StartsWith("stone")) &&
                                (w.obj_def.tool_actions.HasToolK(ItemDefinition.ItemType.Axe) ||
                                 w.obj_def.tool_actions.HasToolK(ItemDefinition.ItemType.Pickaxe)))
                    .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("found a tree or stone", target != null);
                if (target == null) { Bot.Publish("gather", "none"); yield break; }
                long uid = target.unique_id; string objId = target.obj_id; Vector2 at = target.pos;
                float hp0 = target.hp;
                me.energy = me.data.GetParam("max_energy", 100f);
                int dropsBefore = DropsNear(at);
                Func<int> bagTotal = () => me.data.inventory.Where(i => i != null && !i.IsEmpty()).Sum(i => i.value);
                int bagBefore = bagTotal();
                yield return WalkUpTo(target);
                var inter = me.components.interaction;
                Bot.Step($"info target {objId}#{uid} hp={F(hp0)} at {at}; nearest={inter.nearest?.obj_id} tools={string.Join(",", target.obj_def.tool_actions.action_tools)}");
                Bot.Publish("gather", $"{uid}|{objId}|{F(hp0)}");
                yield return Bot.WaitRemote(Other, "watching", "1", 30f);
                me.temp_do_work = true;   // holding the work key
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (!Gone(uid, objId) && UnityEngine.Time.realtimeSinceStartup - t0 < 60f)
                {
                    if (me.energy < 10f) me.energy = me.data.GetParam("max_energy", 100f);
                    yield return null;
                }
                me.temp_do_work = false;
                Bot.Check($"{objId} destroyed by working", Gone(uid, objId), $"after {UnityEngine.Time.realtimeSinceStartup - t0:F1}s now: {Describe(uid)}");
                Diag.Destroyed.Add(uid);
                yield return StaysGone(uid, objId, 5f);
                int drops = DropsNear(at) - dropsBefore;
                int toBag = bagTotal() - bagBefore;
                Bot.Step($"info resources on the ground: {drops}, straight to the bag: {toBag}");
                Bot.Check("resources received (ground or bag)", drops + toBag > 0, $"+{drops}/+{toBag}");
                Bot.Publish("gather_done", $"{drops}|{Describe(uid)}");
                yield return Bot.WaitRemote(Other, "gather_seen", "1", 40f);
            }
            else
            {
                yield return Bot.WaitUntil("gatherer picked a target", () => Bot.Remote(gatherer, "gather") != null, 60f);
                if (Bot.Remote(gatherer, "gather") == "none") yield break;
                var p = Bot.Remote(gatherer, "gather").Split('|');
                long uid = long.Parse(p[0]); string objId = p[1];
                var target = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
                Bot.Check("same object exists here", target != null && target.obj_id == objId, Describe(uid));
                if (target == null) { Bot.Publish("watching", "1"); Bot.Publish("gather_seen", "1"); yield break; }
                Vector2 at = target.pos;
                float hp0 = target.hp, minHp = hp0;
                int dropsBefore = DropsNear(at);
                Bot.Publish("watching", "1");
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(gatherer, "gather_done") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 75f)
                {
                    var w = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
                    if (w != null && !w.is_removed && w.obj_id == objId) minHp = Mathf.Min(minHp, w.hp);
                    yield return null;
                }
                Bot.Step($"info hp seen here: {F(hp0)} -> min {F(minHp)}; now {Describe(uid)}");
                Bot.Check("saw the hp go down", minHp < hp0 || Gone(uid, objId));
                yield return Bot.WaitUntil($"{objId} destroyed here too", () => Gone(uid, objId), 10f);
                Diag.Destroyed.Add(uid);
                yield return StaysGone(uid, objId, 5f);
                var done = (Bot.Remote(gatherer, "gather_done") ?? "0|").Split('|');
                int theirs = int.Parse(done[0]);
                yield return Bot.WaitUntil("same resources on the ground here", () => DropsNear(at) - dropsBefore >= theirs, 10f);
                Bot.Step($"info drops here +{DropsNear(at) - dropsBefore} there +{theirs}; there now: {done[1]}; here now: {Describe(uid)}");
                Bot.Publish("gather_seen", "1");
            }
        }

        private static readonly System.Reflection.FieldInfo LastAttackF =
            HarmonyLib.AccessTools.Field(typeof(BaseCharacterComponent), "_last_pressed_attack_time");

        /// <summary>The host spawns a monster next to the fighter; both see it, the fighter
        /// kills it with the sword (holding the attack key), both see it die.</summary>
        private static string V(Vector2 v) => F(v.x) + ";" + F(v.y);
        private static Vector2 ParseV(string s) { var a = s.Split(';'); return new Vector2(PF(a[0]), PF(a[1])); }

        private static IEnumerator Combat(int fighter)
        {
            string ann = null;
            if (Bot.IsHost)
            {
                yield return Bot.WaitUntil("fighter ready", () => Bot.Remote(fighter, "ready") != null || Plugin.Player == fighter, 30f);
                Vector2 near = Plugin.Player == fighter ? (Vector2)MainGame.me.player.pos : ParseV(Bot.Remote(fighter, "ready"));
                var defs = GameBalance.me.objs_data.Where(d => d != null && d.type == ObjectDefinition.ObjType.Mob).Select(d => d.id).ToList();
                Bot.Step("info mob definitions: " + string.Join(",", defs.Take(30)));
                string mobId = new[] { "bat", "slime", "mob_bat", "mob_slime", "rat", "spider" }.FirstOrDefault(defs.Contains)
                               ?? defs.FirstOrDefault(d => !d.Contains("boss") && !d.Contains("zombie"));
                Bot.Check("found a monster definition", mobId != null);
                if (mobId == null) { Bot.Publish("mob", "none"); Bot.Publish("fight_done", "skip"); yield break; }
                var mob = WorldMap.SpawnWGO(MainGame.me.world_root, mobId, (Vector3)(near + new Vector2(150f, 0f)));
                Bot.Check("monster spawned on host", mob != null && mob.unique_id > 0, mob == null ? "" : $"{mob.obj_id}#{mob.unique_id} hp={F(mob.hp)}");
                if (mob == null) { Bot.Publish("mob", "none"); Bot.Publish("fight_done", "skip"); yield break; }
                ann = $"{mob.unique_id}|{mob.obj_id}";
                Bot.Publish("mob", ann);
            }
            else
            {
                Bot.Publish("ready", V(MainGame.me.player.pos));
                yield return Bot.WaitUntil("monster announced", () => Bot.Remote(1, "mob") != null, 40f);
                ann = Bot.Remote(1, "mob");
            }
            if (ann == null || ann == "none") yield break;
            var parts = ann.Split('|');
            long uid = long.Parse(parts[0]); string id = parts[1];
            yield return Bot.WaitUntil("monster exists here", () => WorldMap.GetWorldGameObjectByUniqueId(uid, false) != null, 20f);
            var m = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
            if (m == null) { Bot.Publish("fight_done", "skip"); yield break; }
            float hp0 = m.hp;
            Bot.Step($"info monster here: {Describe(uid)}");

            if (Plugin.Player == fighter)
            {
                yield return Fight(uid, id, 60f);
                yield return StaysGone(uid, id, 4f);
                Bot.Publish("fight_done", "1");
            }
            else
            {
                float minHp = hp0;
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(fighter, "fight_done") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 75f)
                {
                    var w = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
                    if (w != null && !w.is_removed) minHp = Mathf.Min(minHp, w.hp);
                    yield return null;
                }
                Bot.Step($"info monster hp seen here: {F(hp0)} -> min {F(minHp)}; now {Describe(uid)}");
                Bot.Check("saw the monster take damage", minHp < hp0 || Gone(uid, id));
                yield return Bot.WaitUntil("monster dead here too", () => Gone(uid, id), 10f);
                yield return StaysGone(uid, id, 4f);
            }
        }

        /// <summary>Fight a monster like a player: keep close and side-on, face it, hold attack.</summary>
        private static IEnumerator Fight(long uid, string id, float seconds)
        {
            {
                var me = MainGame.me.player;
                var sword = me.GetEquippedWeapon();
                Bot.Step($"info weapon={sword?.id} hp={F(me.hp)} energy={F(me.energy)}");
                float lastLog = 0f;
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (!Gone(uid, id) && UnityEngine.Time.realtimeSinceStartup - t0 < seconds)
                {
                    var m = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
                    if (m == null) break;
                    Vector2 d = m.pos - (Vector2)me.pos;
                    Vector2 face = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y));
                    if (d.magnitude > 80f) me.PlaceAtPos(m.pos - face * 60f);   // keep up with it, side-on
                    var pc = MainGame.me.player_char;
                    Direction want = face.x > 0 ? Direction.Right : face.x < 0 ? Direction.Left : face.y > 0 ? Direction.Up : Direction.Down;
                    bool facing = pc.anim_direction == want;
                    // Like a player: let go of attack, wait for the swing to end, turn, attack again.
                    if (!facing && !pc.attack.performing_attack) pc.LookAt(m);
                    if (facing) LastAttackF.SetValue(pc, UnityEngine.Time.time);   // holding the attack key
                    if (UnityEngine.Time.realtimeSinceStartup - lastLog > 2f)
                    {
                        lastLog = UnityEngine.Time.realtimeSinceStartup;
                        Bot.Step($"info fight: dist={d.magnitude:F0} face={face} anim_dir={pc.anim_direction} attacking={pc.attack.performing_attack} guis={BaseGUI.opened_windows.Count} mob={Describe(uid)}");
                    }
                    if (me.energy < 10f) me.energy = me.data.GetParam("max_energy", 100f);
                    if (me.hp < me.data.GetParam("max_hp", 100f) * 0.3f) me.hp = me.data.GetParam("max_hp", 100f);
                    yield return null;
                }
                Bot.Check($"killed the {id}", Gone(uid, id), $"after {UnityEngine.Time.realtimeSinceStartup - t0:F1}s now {Describe(uid)}");
            }
        }

        // ------------------------------------------------------------------ a fresh joiner can still pick things up
        private static IEnumerator JoinerInventory()
        {
            yield return Bot.Wait(3f);   // personal grants are delivered right after joining
            var data = MainGame.me.player.data;
            var bag = data.inventory.Where(i => i != null && !i.IsEmpty()).ToList();
            var belt = (data.secondary_inventory ?? new List<Item>()).Where(i => i != null && !i.IsEmpty()).ToList();
            Bot.Step($"info bag {bag.Count}/{data.inventory_size}: " + string.Join(",", bag.Select(i => i.id + "x" + i.value)));
            Bot.Step("info toolbelt: " + string.Join(",", belt.Select(i => i.id)));
            var dupTools = bag.Where(i => i.definition != null && i.definition.stack_count == 1 && belt.Any(b => b.id == i.id)).Select(i => i.id).ToList();
            Bot.Check("no tool both on the toolbelt and in the bag", dupTools.Count == 0, string.Join(",", dupTools));
            Bot.Check("bag has free slots (>= 4)", data.inventory_size - bag.Count >= 4, $"{data.inventory_size - bag.Count} free");
            Bot.Check("tools on the toolbelt", belt.Any(i => i.id.StartsWith("shovel")) && belt.Any(i => i.id.StartsWith("axe")), string.Join(",", belt.Select(i => i.id)));
            // Story items exist once: the joiner must not hold a copy of the host's keys/pages.
            Bot.Publish("bag", string.Join(",", bag.Select(i => i.id)));
            yield return Bot.WaitUntil("other side's bag", () => Bot.Remote(Other, "bag") != null, 30f);
            if (!Bot.IsHost)
            {
                Func<string, bool> unique = id => id.StartsWith("quest_key") || id.StartsWith("story:") || id.StartsWith("key_");
                var hostUnique = new HashSet<string>(Bot.Remote(1, "bag").Split(',').Where(unique));
                var copied = bag.Select(i => i.id).Where(hostUnique.Contains).ToList();
                Bot.Step("info host's unique story items: " + string.Join(",", hostUnique));
                Bot.Check("joiner has no copy of the host's story items", copied.Count == 0, string.Join(",", copied));
                // ShareStoryRewardsWithPartner=false (default): no tutorial/story rewards copied at all.
                Bot.Check("joiner got no copied story rewards (empty bag)", bag.Count == 0, string.Join(",", bag.Select(i => i.id)));
            }
        }
    }
}
