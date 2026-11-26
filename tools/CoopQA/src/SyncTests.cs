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
            new KeyValuePair<string, Func<IEnumerator>>("scene_client", () => Scene(initiator: 2)),
            new KeyValuePair<string, Func<IEnumerator>>("scene_host", () => Scene(initiator: 1)),
            // Take an organ out of the body on the table: the host the heart, the client the brain.
            new KeyValuePair<string, Func<IEnumerator>>("autopsy_host", () => Craft(1, w => w.is_autopsy_table, c => c.id.EndsWith(":heart"), StartAutopsy)),
            new KeyValuePair<string, Func<IEnumerator>>("autopsy_client", () => Craft(2, w => w.is_autopsy_table, c => c.id.EndsWith(":brain"), StartAutopsy)),
            // Replace a grave's wooden fence with a stone one (host) / another stone one (client).
            new KeyValuePair<string, Func<IEnumerator>>("grave_host", () => Craft(1, IsGrave, c => c.id == "set_grave_bot_stn_1", StartGravePart)),
            new KeyValuePair<string, Func<IEnumerator>>("grave_client", () => Craft(2, IsGrave, c => c.id == "set_grave_bot_stn_2", StartGravePart)),
            new KeyValuePair<string, Func<IEnumerator>>("trade_host", () => Trade(1)),
            new KeyValuePair<string, Func<IEnumerator>>("trade_client", () => Trade(2)),
            new KeyValuePair<string, Func<IEnumerator>>("vendor_day_end", VendorDayEnd),
            new KeyValuePair<string, Func<IEnumerator>>("fishing_host", () => Fishing(1)),
            new KeyValuePair<string, Func<IEnumerator>>("fishing_client", () => Fishing(2)),
            new KeyValuePair<string, Func<IEnumerator>>("build_host", () => Build(1)),
            new KeyValuePair<string, Func<IEnumerator>>("build_client", () => Build(2)),
            new KeyValuePair<string, Func<IEnumerator>>("workers", Workers),
            new KeyValuePair<string, Func<IEnumerator>>("garden", Garden),
            new KeyValuePair<string, Func<IEnumerator>>("bees", Bees),
            new KeyValuePair<string, Func<IEnumerator>>("beehouse", BeeHouse),
            new KeyValuePair<string, Func<IEnumerator>>("sermon", Sermon),
            new KeyValuePair<string, Func<IEnumerator>>("porters", Porters),
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
            new KeyValuePair<string, Func<IEnumerator>>("garden_probe", GardenProbe),
            new KeyValuePair<string, Func<IEnumerator>>("bees_probe", BeesProbe),
            new KeyValuePair<string, Func<IEnumerator>>("scene_probe", SceneProbe),
            new KeyValuePair<string, Func<IEnumerator>>("sermon_probe", SermonProbe),
            new KeyValuePair<string, Func<IEnumerator>>("porter_probe", PorterProbe),
            // Put first in a suite filter: the tests after it run through repeated 4 s link outages.
            new KeyValuePair<string, Func<IEnumerator>>("net_hiccups", NetHiccups),
        };

        private static int Other => Bot.IsHost ? 2 : 1;

        private static IEnumerator GardenProbe()
        {
            if (!Bot.IsHost) yield break;
            var me = MainGame.me.player;
            var gardens = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null &&
                                                   (w.obj_id.Contains("garden") || w.obj_id.Contains("bed_")))
                                       .OrderBy(w => (w.pos - me.pos).sqrMagnitude).ToList();
            Bot.Step("info garden objects: " + string.Join(",", gardens.GroupBy(w => w.obj_id).Select(g => g.Key + "x" + g.Count())));
            foreach (var w in gardens.GroupBy(w => w.obj_id).Select(g => g.First()).Take(25))
            {
                var crafts = GameBalance.me.craft_data.Where(c => c != null && c.craft_in != null && c.craft_in.Contains(w.obj_id)).Take(8)
                    .Select(c => $"{c.id}[needs {string.Join("+", c.needs.Select(n => n.id + "x" + n.value))} -> {string.Join("+", c.output.Select(o => o.id))} t={c.craft_time?.EvaluateFloat(w, me)} vis={MainGame.me.save.IsCraftVisible(c)}]");
                var objc = GameBalance.me.craft_obj_data.Where(c => c != null && c.builder_ids != null && c.builder_ids.Contains(w.obj_id)).Take(4).Select(c => c.id);
                Bot.Step($"info {w.obj_id}#{w.unique_id} @{V(w.pos)} dist={(w.pos - me.pos).magnitude / 96f:F0}t interaction={w.obj_def?.interaction_type} has_craft={w.obj_def?.has_craft} crafts: {string.Join(" ", crafts)} objcrafts: {string.Join(",", objc)}");
            }
            var seeds = GameBalance.me.items_data.Where(i => i.id.Contains("seed")).Select(i => i.id).Take(30);
            Bot.Step("info seed items: " + string.Join(",", seeds));
            Bot.Step("info bag: " + Items(me.data.inventory));
        }

        private static IEnumerator BeesProbe()
        {
            if (!Bot.IsHost) yield break;
            var me = MainGame.me.player;
            Func<string, bool> beeish = id => id != null && (id.Contains("bee") || id.Contains("hive") || id.Contains("honey") || id.Contains("wax"));
            var world = WorldMap.objs.Where(w => w != null && !w.is_removed && beeish(w.obj_id)).OrderBy(w => (w.pos - me.pos).sqrMagnitude).ToList();
            Bot.Step("info bee objects in the world: " + string.Join(",", world.GroupBy(w => w.obj_id).Select(g => g.Key + "x" + g.Count())));
            foreach (var w in world.GroupBy(w => w.obj_id).Select(g => g.First()).Take(12))
            {
                var crafts = GameBalance.me.craft_data.Where(c => c != null && c.craft_in != null && c.craft_in.Contains(w.obj_id)).Take(6)
                    .Select(c => $"{c.id}[auto={c.is_auto} hidden={c.hidden} needs={string.Join("+", c.needs.Select(n => n.id + "x" + n.value))} -> {string.Join("+", c.output.Select(o => o.id + "x" + o.value))} vis={MainGame.me.save.IsCraftVisible(c)}]");
                Bot.Step($"info {w.obj_id}#{w.unique_id} @{V(w.pos)} dist={(w.pos - me.pos).magnitude / 96f:F0}t zone={w.GetMyWorldZone()?.id} interaction={w.obj_def?.interaction_type} has_craft={w.obj_def?.has_craft} " +
                         $"crafting={w.components?.craft?.is_crafting} craft={w.components?.craft?.current_craft?.id} progress={F(w.progress)} inv=[{Items(w.data.inventory)}] " +
                         $"tools=[{string.Join(",", w.obj_def?.tool_actions?.action_tools ?? new List<ItemDefinition.ItemType>())}] drop=[{string.Join(",", (w.obj_def?.drop_items ?? new List<Item>()).Select(i => i.id))}] crafts: {string.Join(" ", crafts)}");
            }
            Bot.Step("info bee object defs: " + string.Join(",", GameBalance.me.objs_data.Where(o => beeish(o.id)).Select(o => o.id)));
            Bot.Step("info bee items: " + string.Join(",", GameBalance.me.items_data.Where(i => beeish(i.id)).Select(i => i.id)));
            foreach (var c in GameBalance.me.craft_data.Where(c => c != null && (beeish(c.id) || c.output.Any(o => beeish(o.id)) || c.needs.Any(n => beeish(n.id)))).Take(30))
                Bot.Step($"info craft {c.id} in=[{string.Join(",", c.craft_in ?? new List<string>())}] auto={c.is_auto} hidden={c.hidden} needs={string.Join("+", c.needs.Select(n => n.id + "x" + n.value))} -> {string.Join("+", c.output.Select(o => o.id + "x" + o.value))} vis={MainGame.me.save.IsCraftVisible(c)}");
            foreach (var c in GameBalance.me.craft_obj_data.Where(c => c != null && (beeish(c.id) || c.out_obj != null && beeish(c.out_obj))).Take(10))
                Bot.Step($"info objcraft {c.id} -> {c.out_obj} builders=[{string.Join(",", c.builder_ids ?? new List<string>())}] needs={string.Join("+", c.needs.Select(n => n.id + "x" + n.value))} vis={MainGame.me.save.IsCraftVisible(c)}");
        }

        /// <summary>Which multi-NPC story scenes this save could play: where their NPCs are and
        /// which quests are running.</summary>
        private static IEnumerator SceneProbe()
        {
            if (!Bot.IsHost) yield break;
            var me = MainGame.me.player;
            Bot.Step($"info day={MainGame.me.save.day} dow={MainGame.me.save.day_of_week} quests: " +
                     string.Join(",", MainGame.me.save.quests.GetCurrentQuests().Select(q => q.definition?.id)));
            foreach (var id in new[] { "npc_inquisitor", "npc_guard_3", "npc_citizen_1", "npc_citizen_2", "npc_citizen_5", "npc_burger_eater_1", "witch_pylon", "vendor_stall",
                                       "npc_farmer", "npc_farmers_son", "npc_alice", "npc_donkey", "donkey_cart", "npc_bishop", "npc_actress", "npc_actor" })
            {
                var ws = WorldMap.objs.Where(w => w != null && !w.is_removed && (w.obj_id == id || w.custom_tag == id)).ToList();
                Bot.Step($"info {id}: " + (ws.Count == 0 ? "none" : string.Join(" ", ws.Take(3).Select(w => $"{w.obj_id}#{w.unique_id}@{V(w.pos)} tag={w.custom_tag} active={w.gameObject.activeInHierarchy} dist={(w.pos - me.pos).magnitude / 96f:F0}t"))));
            }
            yield break;
        }

        private static IEnumerator SermonProbe()
        {
            if (!Bot.IsHost) yield break;
            var me = MainGame.me.player;
            var pulpit = WorldMap.GetWorldGameObjectByCustomTag("church_pulpit");
            Bot.Step($"info day={MainGame.me.save.day} dow={MainGame.me.save.day_of_week} church_level={me.GetParam("church_level")} prayed_this_week={me.GetParam("prayed_this_week")} faith={me.data.GetTotalCount("faith")} money={me.data.money}");
            Bot.Step($"info pulpit: {(pulpit == null ? "none" : $"{pulpit.obj_id}#{pulpit.unique_id} @{V(pulpit.pos)} dist={(pulpit.pos - me.pos).magnitude / 96f:F0}t interaction={pulpit.obj_def?.interaction_type} has_craft={pulpit.obj_def?.has_craft} zone={pulpit.GetMyWorldZone()?.id} q={pulpit.GetMyWorldZone()?.GetTotalQuality()}")}");
            var church = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && (w.obj_id.Contains("pulpit") || w.obj_id.Contains("church") || w.obj_id.Contains("pray")))
                                      .GroupBy(w => w.obj_id).Select(g => g.Key + "x" + g.Count());
            Bot.Step("info church objects: " + string.Join(",", church));
            var preach = GameBalance.me.items_data.Where(i => i.type == ItemDefinition.ItemType.Preach).Select(i => i.id);
            Bot.Step("info sermon items: " + string.Join(",", preach));
            var prays = GameBalance.me.craft_data.Where(c => c != null && c.id.StartsWith("pray:")).Take(10)
                .Select(c => $"{c.id}[q={c.needs_quality} sub={c.linked_sub_id} needs={string.Join("+", c.needs.Select(n => n.id + "x" + n.value))} out={string.Join("+", c.output.Select(o => o.id + "x" + o.value))} vis={MainGame.me.save.IsCraftVisible(c)}]");
            Bot.Step("info pray crafts: " + string.Join(" ", prays));
            Bot.Step("info sermons in bag: " + string.Join(",", me.data.inventory.Where(i => i?.definition?.type == ItemDefinition.ItemType.Preach).Select(i => i.id)));
        }

        private static IEnumerator PorterProbe()
        {
            if (!Bot.IsHost) yield break;
            var stations = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_def != null && w.obj_def.type == ObjectDefinition.ObjType.PorterStation).ToList();
            Bot.Step($"info porter stations: {stations.Count}");
            foreach (var st in stations)
            {
                var ps = st.porter_station;
                Bot.Step($"info {st.obj_id}#{st.unique_id} @{V(st.pos)} zone={st.GetMyWorldZone()?.id} state={ps?.state} worker={(st.has_linked_worker ? st.linked_worker.obj_id + "#" + st.linked_worker.unique_id : "-")} src={ps?.source?.id} dst={ps?.destination?.id}");
            }
            foreach (var tp in GameBalance.me.transport_paths.Take(20))
                Bot.Step($"info path {tp.station_wgo_id}: {tp.source_zone_id} -> {tp.destination_zone_id} items={string.Join(",", tp.transport_items.Take(8))}");
            var crafts = GameBalance.me.craft_data.Where(c => c != null && c.id.Contains("porter")).Select(c => $"{c.id}[vis={MainGame.me.save.IsCraftVisible(c)} needs={string.Join("+", c.needs.Select(n => n.id + "x" + n.value))}]");
            Bot.Step("info porter crafts: " + string.Join(" ", crafts));
            Bot.Step("info objdefs: " + string.Join(",", GameBalance.me.objs_data.Where(o => o.type == ObjectDefinition.ObjType.PorterStation).Select(o => o.id)));
            var workers = WorldMap.objs.Where(w => w != null && !w.is_removed && w.worker != null && w.IsWorker()).Select(w => $"{w.obj_id}#{w.unique_id}@{V(w.pos)}");
            Bot.Step("info workers: " + string.Join(" ", workers));
            foreach (var zid in new[] { "stone_workyard", "zombie_sawmill", "mf_wood", "vineyard", "cellar", "cellar_storage", "storage", "player_tavern_cellar" })
            {
                var zone = WorldZone.GetZoneByID(zid, false);
                var gd = WorldMap.GetGDPointByGDTag(zid + "_d");
                var inv = zone == null ? "" : string.Join(" ", WorldMap.objs.Where(w => w != null && !w.is_removed && w.GetMyWorldZone() == zone && w.obj_def != null && w.obj_def.inventory_size > 0)
                                                               .Select(w => $"{w.obj_id}#{w.unique_id}[{w.data.inventory.Count}/{w.obj_def.inventory_size}]").Take(10));
                Bot.Step($"info zone {zid}: {(zone == null ? "missing" : "ok")} gd={(gd == null ? "-" : V(gd.pos))} storages: {inv}");
            }
        }

        /// <summary>Wood per place: "zone:count" for the sawmill, the wood storage and the
        /// porter's backpack, as this side sees them.</summary>
        private static string WoodDigest(long stationUid)
        {
            int Zone(string id)
            {
                var zone = WorldZone.GetZoneByID(id, false);
                return zone == null ? -1 : WorldMap.objs.Where(w => w != null && !w.is_removed && w.GetMyWorldZone() == zone && w.data?.inventory != null && !w.is_player)
                                                        .Sum(w => w.data.inventory.Where(i => i?.id == "wood").Sum(i => i.value));
            }
            var st = WorldMap.GetWorldGameObjectByUniqueId(stationUid, false);
            var z = st?.linked_worker;
            int carried = z?.worker?.GetBackpack()?.inventory?.Where(i => i?.id == "wood").Sum(i => i.value) ?? 0;
            return $"sawmill={Zone("zombie_sawmill")} mf_wood={Zone("mf_wood")} carried={carried} ground={Drops("wood").Sum(d => d.res.value)}";
        }

        private static string PorterInfo(long stationUid)
        {
            var st = WorldMap.GetWorldGameObjectByUniqueId(stationUid, false);
            var z = st?.linked_worker;
            return st == null ? "no station" : $"state={st.porter_station?.state} worker={(z == null ? "-" : z.unique_id + "@" + V(z.pos) + " active=" + z.gameObject.activeInHierarchy)}";
        }

        /// <summary>Zombie logistics: the host builds a porter station at the sawmill with a
        /// stack of wood and gives it a zombie. The wood must arrive at the wood storage once,
        /// both sides must agree on where every log is, and the client must not run its own
        /// copy of the porter (that moved the same wood twice).</summary>
        private static IEnumerator Porters()
        {
            var me = MainGame.me.player;
            const int wood = 4;   // two trips (logs are big: two per load); between them the station waits with its zombie
            // The porter takes minutes; night bats find the idle players meanwhile (the
            // host died here once, and its death scene then held it in the dungeon test).
            Bot.KeepAlive = true;   // Suite resets it
            if (Bot.IsHost)
            {
                var src = WorldMap.GetGDPointByGDTag("zombie_sawmill_d");
                Bot.Require("sawmill point", src != null);
                me.PlaceAtPos((Vector2)src.pos + new Vector2(0f, -200f));
                yield return Bot.Wait(2f);
                // Two free spots inside the sawmill zone, near its porter point.
                var spots = new List<Vector2>();
                for (int r = 1; r <= 8 && spots.Count < 2; r++)
                    foreach (var dir in new[] { Vector2.left, Vector2.up, Vector2.right, Vector2.down })
                    {
                        var at = (Vector2)src.pos + dir * (96f * r);
                        if (WorldZone.GetZoneOfPoint(at)?.id == "zombie_sawmill" && spots.All(o => (o - at).magnitude > 150f)) spots.Add(at);
                        if (spots.Count == 2) break;
                    }
                Bot.Require("room in the sawmill", spots.Count == 2);
                var station = WorldMap.SpawnWGO(MainGame.me.world_root, "porter_station", (Vector3)spots[0]);
                var box = WorldMap.SpawnWGO(MainGame.me.world_root, "mf_timber_1", (Vector3)spots[1]);
                yield return Bot.Wait(1f);
                Bot.Require("station and timber pile in the sawmill", station?.GetMyWorldZone()?.id == "zombie_sawmill" && box?.GetMyWorldZone()?.id == "zombie_sawmill",
                            $"station zone={station?.GetMyWorldZone()?.id} box zone={box?.GetMyWorldZone()?.id}");
                var wdef = GameBalance.me.GetData<ItemDefinition>("wood");
                Bot.Step($"info wood: stack={wdef.stack_count} big={wdef.is_big} crate={wdef.is_crate}");
                int put = 0;
                for (int i = 0; i < wood; i++) if (box.AddToInventory(new Item("wood", 1))) put++;
                bool added = put == wood;
                Bot.Step($"info timber pile {box.obj_id}#{box.unique_id} added={added} inv=[{Items(box.data.inventory)}] size={box.obj_def.inventory_size} zone={box.GetMyWorldZone()?.id}");
                Bot.Step($"info station {station.obj_id}#{station.unique_id} dst={station.porter_station?.destination?.id}; " + WoodDigest(station.unique_id));
                Bot.Publish("station", station.unique_id.ToString());
                yield return Bot.WaitRemote(2, "station_seen", "1", 60f);
                string start = WoodDigest(station.unique_id);
                Bot.Publish("start", start);
                yield return Bot.WaitRemote(2, "start_seen", "1", 60f);

                var def = GameBalance.me.workers_data.FirstOrDefault(d => d.id.Contains("zombie")) ?? GameBalance.me.workers_data.First(d => d.id != "worker_invisible");
                var z = WorldMap.SpawnWGO(MainGame.me.world_root, def.worker_wgo, (Vector3)(station.pos + new Vector2(0f, -96f)));
                MainGame.me.save.workers.CreateNewWorker(z, def.id, MainGame.me.save.GenerateBody(1, 3)).UpdateWorkerLevel();
                var dock = station.GetAvailableDockPointForZombie();
                if (dock != null) { z.transform.position = dock.transform.position; z.RefreshPositionCache(); }
                station.linked_worker = z;
                station.porter_station.state = PorterStation.PorterState.Waiting;   // what the game sets when it docks a worker
                z.SetParam("is_disabled", 0f);
                Bot.Step($"info porter on duty: {PorterInfo(station.unique_id)} dock={dock != null}");
                Bot.Publish("porter", z.unique_id.ToString());

                yield return Bot.WaitUntil("porter set off", () => station.porter_station.state == PorterStation.PorterState.GoingToDestination, 30f);
                Bot.Step("info set off: " + WoodDigest(station.unique_id) + " " + PorterInfo(station.unique_id));
                yield return Bot.WaitUntil("porter delivered everything", () => WoodCarried(station.unique_id) == 0 && WoodDigest(station.unique_id).StartsWith("sawmill=0 "), 480f);
                yield return Bot.Wait(5f);
                string end = WoodDigest(station.unique_id);
                Bot.Step("info delivered: " + end + " " + PorterInfo(station.unique_id));
                Bot.Check("all the wood reached the wood storage", end.StartsWith("sawmill=0 ") && end.Contains("carried=0"), $"{start} -> {end}");
                int Mf(string d) => int.Parse(d.Split(' ')[1].Substring("mf_wood=".Length));
                Bot.Check("delivered once", Mf(end) == Mf(start) + wood, $"{start} -> {end}");
                Bot.Publish("end", end);
                yield return Bot.WaitRemote(2, "compared", "1", 90f);
            }
            else
            {
                yield return Bot.WaitUntil("host built a station", () => Bot.Remote(1, "station") != null, 90f);
                long sid = long.Parse(Bot.Remote(1, "station"));
                yield return Bot.WaitUntil("the station is here", () => WorldMap.GetWorldGameObjectByUniqueId(sid, false) != null, 30f);
                Bot.Publish("station_seen", "1");
                yield return Bot.WaitUntil("host's starting wood", () => Bot.Remote(1, "start") != null, 60f);
                string start = Bot.Remote(1, "start");
                yield return Bot.WaitUntil("the same wood here", () => WoodDigest(sid) == start, 30f);
                Bot.Step("info start here: " + WoodDigest(sid));
                Bot.Publish("start_seen", "1");
                yield return Bot.WaitUntil("host's porter", () => Bot.Remote(1, "porter") != null, 60f);
                string trail = "";
                int maxMf = 0;
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(1, "end") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 560f)
                {
                    string d = WoodDigest(sid);
                    if (!trail.EndsWith(d)) trail += " > " + d;
                    int mfHere = int.Parse(d.Split(' ')[1].Substring("mf_wood=".Length));
                    maxMf = Math.Max(maxMf, mfHere);
                    yield return Bot.Wait(0.25f);
                }
                Bot.Step("info wood here over time:" + trail);
                int mfStart = int.Parse(start.Split(' ')[1].Substring("mf_wood=".Length));
                Bot.Check("never more wood in the storage here than was carried", maxMf <= mfStart + wood, $"max {maxMf}, start {mfStart} + {wood}");
                Bot.Step("info porter here: " + PorterInfo(sid));
                string end = Bot.Remote(1, "end");
                string Places(string d) => d.Substring(0, d.IndexOf(" carried="));
                yield return Bot.WaitUntil("the same wood here at the end (sawmill and storage)", () => Places(WoodDigest(sid)) == Places(end), 30f);
                long zid = long.Parse(Bot.Remote(1, "porter"));
                var zHere = WorldMap.GetWorldGameObjectByUniqueId(zid, false);
                Bot.Check("the porter zombie exists here", zHere != null && !zHere.is_removed, PorterInfo(sid));
                Bot.Check("the station has its porter here", WorldMap.GetWorldGameObjectByUniqueId(sid, false)?.linked_worker?.unique_id == zid, PorterInfo(sid));
                Bot.Step("info end here: " + WoodDigest(sid));
                Bot.Publish("compared", "1");
            }
        }

        private static int WoodCarried(long sid) =>
            WorldMap.GetWorldGameObjectByUniqueId(sid, false)?.linked_worker?.worker?.GetBackpack()?.inventory?.Where(i => i?.id == "wood").Sum(i => i.value) ?? 0;

        private static string SermonState()
        {
            var me = MainGame.me.player;
            var r = PrayLogics.last_pray_result;
            return $"faith={me.data.GetTotalCount("faith")} money={F(me.data.money)} sermons={me.data.GetTotalCount("b_empty")} prayed={me.GetParam("prayed_this_week")} " +
                   $"control={GS.IsPlayerEnable()} last=[{r.pray_craft_id} ok={r.success} ppl={r.people} f={r.faith}+{r.faith_bonus} m={F(r.money)}+{F(r.money_bonus)}]";
        }

        /// <summary>The church donation box(es): "obj#uid:money".</summary>
        private static string ChurchIncome() =>
            string.Join(" ", WorldMap.objs.Where(w => w != null && !w.is_removed && w.unique_id > 0 && w.obj_id != null && w.obj_id.StartsWith("donat_box"))
                                          .OrderBy(w => w.unique_id).Select(w => $"{w.obj_id}#{w.unique_id}:{F(w.GetParam("_money"))}"));

        private static string FaithDiag() =>
            "drops=[" + string.Join(" ", UnityEngine.Object.FindObjectsOfType<DropResGameObject>().Where(d => d != null && d.res != null && d.res.id == "faith")
                                          .Select(d => $"{V(d.transform.position)}x{d.res.value}{(d.is_collected ? "(collected)" : "")}{(d.gameObject.activeInHierarchy ? "" : "(inactive)")}")) +
            "] visitors=[" + string.Join(" ", WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.Contains("church_visitor"))
                                          .Select(w => $"{w.unique_id}:{F(w.GetParam("_money"))}@{V(w.pos)}{(w.gameObject.activeInHierarchy ? "" : "(inactive)")}")) + "]";

        /// <summary>Sermon visitors still in the world (they leave and are removed after donating).</summary>
        private static int Visitors() =>
            WorldMap.objs.Count(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.Contains("church_visitor"));

        private static float BoxMoney() =>
            WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.StartsWith("donat_box")).Sum(w => w.GetParam("_money"));

        /// <summary>A sermon in each direction: one player preaches at the pulpit (opens the pulpit
        /// window, picks a sermon, presses Pray, like the mouse does) while the other sits in the
        /// church. Only the preacher spends the sermon and gets the result; the listener's bag and
        /// purse stay as they were, both get control back, and both see the same donations.</summary>
        private static IEnumerator Sermon()
        {
            var pulpit = WorldMap.GetWorldGameObjectByCustomTag("church_pulpit");
            Bot.Require("church pulpit present", pulpit != null);
            var inter = pulpit.obj_def.GetValidInteraction(pulpit);
            Bot.Step($"info pulpit interaction script={inter?.script} dow={MainGame.me.save.day_of_week} income=[{ChurchIncome()}]");
            yield return SermonRound(preacher: 1);
            yield return SermonRound(preacher: 2);
        }

        private static IEnumerator SermonRound(int preacher)
        {
            var me = MainGame.me.player;
            var pulpit = WorldMap.GetWorldGameObjectByCustomTag("church_pulpit");
            const string sermon = "b_empty";
            string k = "r" + preacher + "_";
            Func<int> myFaith = () => me.data.GetTotalCount("faith");
            if (Plugin.Player == preacher)
            {
                yield return Bot.WaitRemote(Other, k + "seated", "1", 60f);
                yield return Bot.Wait(2f);   // the listener may pick up an earlier drop on the way in
                int faithBase = myFaith() + int.Parse(Bot.Remote(Other, k + "faith")) + Drops("faith").Sum(d => d.res.value);
                Bot.Step("info preacher start: " + FaithDiag());
                yield return WalkUpTo(pulpit);
                me.SetParam("prayed_this_week", 0f);     // this week's sermon may be spent already
                if (me.data.GetTotalCount(sermon) == 0) me.data.AddItem(sermon, 1);
                int have = me.data.GetTotalCount(sermon);
                Bot.Step("info preacher before: " + SermonState());
                float box0 = BoxMoney();
                GUIElements.me.OpenCraftGUI(pulpit);
                yield return Bot.Wait(1f);
                var picker = GUIElements.me.pray_craft.GetComponent<ResourceBasedCraftGUI>();
                Bot.Call(picker, "OnResourcePickerClosed", me.data.inventory.First(i => i?.id == sermon));
                Bot.Step($"info picked: craft={GUIElements.me.pray_craft.pray_craft?.id}");
                picker.OnCraftButtonPressed();
                Bot.Publish(k + "preaching", "1");
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                yield return Bot.WaitUntil("sermon started", () => !GS.IsPlayerEnable(), 20f);
                // Track every visitor's purse and the box until the visitors are gone (diagnostic).
                var lastMoney = new Dictionary<long, string>();
                string boxTrail = ChurchIncome();
                bool overSeen = false;
                while (UnityEngine.Time.realtimeSinceStartup - t0 < 300f)
                {
                    bool over = GS.IsPlayerEnable() && PrayLogics.last_pray_result.pray_craft_id == "pray:" + sermon;
                    if (over && !overSeen) { overSeen = true; Bot.Step("ok   sermon over"); }
                    foreach (var v in WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.Contains("church_visitor")))
                        lastMoney[v.unique_id] = F(v.GetParam("_money")) + "@" + V(v.pos);
                    string box = ChurchIncome();
                    if (!boxTrail.EndsWith(box)) boxTrail += " > " + box;
                    if (over && Visitors() == 0) break;
                    yield return null;
                }
                Bot.Step($"info visitors' last purse: {string.Join(" ", lastMoney.Select(kv => kv.Key + ":" + kv.Value))} box: {boxTrail}");
                var r = PrayLogics.last_pray_result;
                Bot.Step($"info sermon took {UnityEngine.Time.realtimeSinceStartup - t0:F0}s; after: {SermonState()} windows={string.Join(",", BaseGUI.opened_windows.Select(w => w.GetType().Name))}");
                Bot.Check("the sermon was preached", r.people > 0, SermonState());
                Bot.Check("the sermon is kept for next week", me.data.GetTotalCount(sermon) == have, $"{have} -> {me.data.GetTotalCount(sermon)}");
                Bot.Publish(k + "preached", "1");
                // Visitors walk to the donation box, faith drops at the altar; let both settle.
                Bot.Check("the visitors left", Visitors() == 0, FaithDiag());
                float donated = BoxMoney() - box0, promised = r.money + r.money_bonus;
                Bot.Check("the box got the visitors' donations", donated >= promised - 0.005f && donated <= promised + 0.1f, $"{F(donated)} for {F(promised)}");
                int expect = r.success ? r.faith + r.faith_bonus : r.faith;
                Func<int> faithNow = () => { var o = Bot.Remote(Other, k + "faith"); return myFaith() + (o == null ? 0 : int.Parse(o)) + Drops("faith").Sum(d => d.res.value); };
                float s0 = UnityEngine.Time.realtimeSinceStartup;
                while (faithNow() != faithBase + expect && UnityEngine.Time.realtimeSinceStartup - s0 < 30f) yield return Bot.Wait(0.5f);
                yield return Bot.Wait(3f);   // a late duplicate would show up now
                Bot.Step("info preacher end: " + FaithDiag());
                Bot.Check("the sermon's faith arrived once (both bags + ground)", faithNow() == faithBase + expect,
                          $"base {faithBase} + {expect} expected, have mine={myFaith()} partner={Bot.Remote(Other, k + "faith")} ground={Drops("faith").Sum(d => d.res.value)}");
                Bot.Publish(k + "income", ChurchIncome());
                yield return Bot.WaitRemote(Other, k + "done", "1", 60f);
            }
            else
            {
                me.PlaceAtPos(pulpit.pos + new Vector2(60f, -250f));   // in the pews
                yield return Bot.Wait(1f);
                float money0 = me.data.money;
                string last0 = PrayLogics.last_pray_result.pray_craft_id + "/" + PrayLogics.last_pray_result.people;
                Bot.Step("info listener start: " + FaithDiag());
                Bot.Publish(k + "faith", myFaith().ToString());
                Bot.Publish(k + "seated", "1");
                float t0 = UnityEngine.Time.realtimeSinceStartup, pub = 0f;
                bool sawCinematic = false;
                while (Bot.Remote(preacher, k + "preached") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 360f)
                {
                    sawCinematic |= !GS.IsPlayerEnable();
                    if (UnityEngine.Time.realtimeSinceStartup - pub > 0.5f) { Bot.Publish(k + "faith", myFaith().ToString()); pub = UnityEngine.Time.realtimeSinceStartup; }
                    yield return null;
                }
                Bot.Step($"info listener: sawCinematic={sawCinematic} pos={V(me.pos)} after: {SermonState()}");
                yield return Bot.WaitUntil("listener has control", GS.IsPlayerEnable, 60f);
                Bot.Check("listener's money unchanged", Mathf.Abs(me.data.money - money0) < 0.001f, $"{money0} -> {me.data.money}");
                Bot.Check("the sermon ran only for the preacher", PrayLogics.last_pray_result.pray_craft_id + "/" + PrayLogics.last_pray_result.people == last0);
                while (Bot.Remote(preacher, k + "income") == null)
                {
                    Bot.Publish(k + "faith", myFaith().ToString());
                    yield return Bot.Wait(0.5f);
                }
                yield return Bot.WaitUntil("same donation box here", () => ChurchIncome() == Bot.Remote(preacher, k + "income"), 30f);
                Bot.Step($"info donations: {ChurchIncome()} faith in my bag {myFaith()} " + FaithDiag());
                Bot.Publish(k + "done", "1");
            }
        }

        private static string GardenInfo(WorldGameObject w) => w == null ? "none" :
            $"{w.obj_id}#{w.unique_id} crafting={w.components?.craft?.is_crafting} craft={w.components?.craft?.current_craft?.id} progress={F(w.progress)} hp={F(w.hp)} inv=[{string.Join(",", w.data.inventory.Select(i => i.id + "x" + i.value))}]";

        /// <summary>A garden bed through a season: the client plants wheat, the host sees it grow
        /// (fast-forwarded on the host, which runs the world), both see it ripen, the client
        /// harvests it and gets the crop, both see an empty bed again.</summary>
        private static IEnumerator Garden()
        {
            var me = MainGame.me.player;
            const string seed = "wheat_seed", plantCraft = "garden_wheat_planting_1";
            Func<long, WorldGameObject> bed = uid => WorldMap.GetWorldGameObjectByUniqueId(uid, false);
            if (!Bot.IsHost)
            {
                var craft = GameBalance.me.GetDataOrNull<CraftDefinition>(plantCraft);
                var empty = WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id == "garden_empty" && w.unique_id > 0 &&
                                                     w.components?.craft != null && !w.components.craft.is_crafting)
                                         .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("an empty garden bed and the wheat recipe", empty != null && craft != null);
                if (empty == null || craft == null) { Bot.Publish("bed", "none"); yield break; }
                int need = craft.needs.First(n => n.id == seed).value;
                me.data.AddItem(seed, need);   // stands in for seeds bought earlier
                int seedsBefore = me.data.GetTotalCount(seed, true);
                long uid = empty.unique_id;
                Bot.Publish("bed", uid.ToString());
                yield return WalkUpTo(empty);
                Bot.Step($"info at the bed: nearest={me.components.interaction.nearest?.obj_id} {GardenInfo(empty)}");
                bool started = empty.components.craft.CraftAsPlayer(craft);   // picking the seeds in the garden window
                me.temp_do_work = true;
                yield return Bot.WaitUntil("wheat planted", () => bed(uid)?.obj_id == "garden_wheat", 20f);
                me.temp_do_work = false;
                Bot.Step($"info planted (started={started}): {GardenInfo(bed(uid))}");
                Bot.Check("the seeds left my bag", me.data.GetTotalCount(seed, true) == seedsBefore - need,
                          $"{me.data.GetTotalCount(seed, true)} left of {seedsBefore}");
                Bot.Publish("planted", "1");
                yield return Bot.WaitRemote(1, "planted_seen", "1", 40f);

                yield return Bot.WaitUntil("host grew it", () => Bot.Remote(1, "grown") != null, 90f);
                yield return Bot.WaitUntil("ripe here too", () => bed(uid)?.obj_id == "garden_wheat_ready", 30f);
                Bot.Step("info ripe: " + GardenInfo(bed(uid)) + $" tools=[{string.Join(",", bed(uid)?.obj_def?.tool_actions?.action_tools ?? new List<ItemDefinition.ItemType>())}] interaction={bed(uid)?.obj_def?.interaction_type}");

                // Harvest: stand at it and work (the action key), like a player.
                string[] crops = { "wheat", "wheat_seed", "wheat_crop", "crop_waste" };   // the harvest is random: crop, seeds or waste
                Func<int> have = () => crops.Sum(c => me.data.GetTotalCount(c, true));
                Func<int> dropsNear = () => crops.Sum(c => Drops(c).Count(d => Vector2.Distance(d.transform.position, bed(uid)?.pos ?? me.pos) < 400f));
                int haveBefore = have(), dropsBefore = dropsNear();
                yield return WalkUpTo(bed(uid));
                var ripe = bed(uid);
                if (ripe != null && ripe.obj_def?.interaction_type != ObjectDefinition.InteractionType.None)
                    ripe.Interact(me, interaction_start: true);
                me.temp_do_work = true;
                float h0 = UnityEngine.Time.realtimeSinceStartup;
                while (bed(uid)?.obj_id == "garden_wheat_ready" && UnityEngine.Time.realtimeSinceStartup - h0 < 30f)
                {
                    if (me.energy < 10f) me.energy = 100f;
                    yield return null;
                }
                me.temp_do_work = false;
                Bot.Check("harvested", bed(uid)?.obj_id != "garden_wheat_ready", GardenInfo(bed(uid)));
                yield return Bot.Wait(2f);
                Bot.Step($"info after harvest: {GardenInfo(bed(uid))} bag+{have() - haveBefore} drops+{dropsNear() - dropsBefore}");
                Bot.Check("got the crop (bag or ground)", have() - haveBefore + dropsNear() - dropsBefore > 0,
                          $"bag [{Items(me.data.inventory)}]");
                Bot.Publish("harvested", bed(uid)?.obj_id ?? "none");
                yield return Bot.WaitRemote(1, "harvest_seen", "1", 40f);
            }
            else
            {
                yield return Bot.WaitUntil("client picked a bed", () => Bot.Remote(2, "bed") != null, 60f);
                if (Bot.Remote(2, "bed") == "none") yield break;
                long uid = long.Parse(Bot.Remote(2, "bed"));
                yield return Bot.WaitUntil("client planted", () => Bot.Remote(2, "planted") != null, 60f);
                yield return Bot.WaitUntil("wheat growing here", () => bed(uid)?.obj_id == "garden_wheat", 20f);
                Bot.Step("info here: " + GardenInfo(bed(uid)));
                Bot.Publish("planted_seen", "1");
                // A day of growing, fast-forwarded: the host runs the growth.
                var w = bed(uid);
                yield return Bot.WaitUntil("it is growing", () => w != null && w.components.craft.is_crafting &&
                                                                  (w.components.craft.current_craft?.id ?? "").EndsWith("_growing"), 20f);
                Bot.Step($"info growing: {GardenInfo(w)} auto={w.components.craft.current_craft?.is_auto} hidden={w.components.craft.current_craft?.hidden}");
                // Fast-forward the growth. The craft can rewind a progress set from outside
                // (it keeps its own timing), so keep nudging it until the bed ripens.
                yield return Bot.WaitUntil("ripe here", () =>
                {
                    var b = bed(uid);
                    if (b == null) return false;
                    if (b.obj_id == "garden_wheat_ready") return true;
                    if (b.components.craft.is_crafting && b.progress < 0.999f) b.progress = 0.999f;
                    return false;
                }, 30f);
                Bot.Publish("grown", "1");
                Vector2 at = bed(uid)?.pos ?? me.pos;
                Func<int> hostCrop = () => me.data.GetTotalCount("wheat_crop", true) +
                                           Drops("wheat_crop").Count(d => Vector2.Distance(d.transform.position, at) < 400f);
                int cropBefore = hostCrop();
                yield return Bot.WaitUntil("client harvested", () => Bot.Remote(2, "harvested") != null, 90f);
                string theirs = Bot.Remote(2, "harvested");
                yield return Bot.WaitUntil("the bed looks the same here", () => (bed(uid)?.obj_id ?? "none") == theirs, 20f);
                yield return Bot.Wait(2f);
                Bot.Step("info after the client's harvest: " + GardenInfo(bed(uid)));
                Bot.Check("the crop went only to the client (none here, none on the ground)", hostCrop() == cropBefore,
                          $"{cropBefore} -> {hostCrop()}");
                Bot.Publish("harvest_seen", "1");
            }
        }

        private static readonly string[] BeeLoot = { "honey", "bee", "beeswax" };

        /// <summary>A wild hive in a tree: the host runs the world, so it fills the hive
        /// (fast-forwarded); both see it full; the client takes the honey by hand and gets
        /// honey, bees and wax once; the host gets nothing and both see the same tree after.</summary>
        private static IEnumerator Bees()
        {
            var me = MainGame.me.player;
            Func<long, WorldGameObject> tree = uid => WorldMap.GetWorldGameObjectByUniqueId(uid, false);
            Func<WorldGameObject, bool> full = w => w != null && !w.is_removed && w.obj_id.StartsWith("tree_3_") && w.obj_id.EndsWith("_bees_done");
            Func<int> loot = () => BeeLoot.Sum(id => me.data.GetTotalCount(id, true));
            if (Bot.IsHost)
            {
                foreach (var id in new[] { "beehouse_place", "beehouse_1", "beehouse_2" })
                    Bot.Step($"info {id} crafts: " + string.Join(" ", GameBalance.me.craft_data.Where(c => c != null && c.craft_in != null && c.craft_in.Contains(id))
                        .Select(c => $"{c.id}[auto={c.is_auto} hidden={c.hidden} needs={string.Join("+", c.needs.Select(n => n.id + "x" + n.value))} -> {string.Join("+", c.output.Select(o => o.id + "x" + o.value))} end={c.end_script}]")) +
                        $" interaction={GameBalance.me.GetDataOrNull<ObjectDefinition>(id)?.interaction_type} tools=[{string.Join(",", GameBalance.me.GetDataOrNull<ObjectDefinition>(id)?.tool_actions?.action_tools ?? new List<ItemDefinition.ItemType>())}]");
                // A hive that is still filling, nearest first.
                var hive = WorldMap.objs.Where(w => w != null && !w.is_removed && w.unique_id > 0 && w.obj_id != null && w.obj_id.StartsWith("tree_3_") &&
                                                    w.obj_id.EndsWith("_bees") && w.components?.craft != null && w.components.craft.is_crafting)
                                        .OrderBy(w => (w.pos - me.pos).sqrMagnitude).FirstOrDefault();
                Bot.Check("a wild hive that is filling", hive != null);
                if (hive == null) { Bot.Publish("hive", "none"); yield break; }
                long uid = hive.unique_id;
                Bot.Step("info hive: " + GardenInfo(hive));
                Bot.Publish("hive", uid.ToString());
                yield return Bot.WaitRemote(2, "hive_seen", "1", 40f);
                // Fast-forward the filling, as the garden test does.
                yield return Bot.WaitUntil("the hive filled", () =>
                {
                    var t = tree(uid);
                    if (full(t)) return true;
                    if (t != null && t.components?.craft != null && t.components.craft.is_crafting && t.progress < 0.999f) t.progress = 0.999f;
                    return false;
                }, 30f);
                Bot.Step("info full: " + GardenInfo(tree(uid)));
                Bot.Publish("full", tree(uid)?.obj_id ?? "none");
                Vector2 at = tree(uid)?.pos ?? me.pos;
                int mine0 = loot();
                yield return Bot.WaitUntil("client took the honey", () => Bot.Remote(2, "taken") != null, 90f);
                string theirs = Bot.Remote(2, "taken");
                yield return Bot.WaitUntil("the tree looks the same here", () => (tree(uid)?.obj_id ?? "none") == theirs, 20f);
                yield return Bot.Wait(2f);
                Bot.Step("info after the client's harvest: " + GardenInfo(tree(uid)));
                Bot.Check("the honey went only to the client (my bag unchanged)", loot() == mine0, $"{mine0} -> {loot()}");
                yield return Bot.WaitUntil("the hive fills again (host runs it)", () => tree(uid)?.components?.craft?.is_crafting == true, 10f);
                Bot.Publish("taken_seen", "1");
            }
            else
            {
                yield return Bot.WaitUntil("host picked a hive", () => Bot.Remote(1, "hive") != null, 60f);
                if (Bot.Remote(1, "hive") == "none") yield break;
                long uid = long.Parse(Bot.Remote(1, "hive"));
                Bot.Check("the same hive here", tree(uid) != null && tree(uid).obj_id.EndsWith("_bees"), GardenInfo(tree(uid)));
                Bot.Publish("hive_seen", "1");
                yield return Bot.WaitUntil("host filled it", () => Bot.Remote(1, "full") != null, 60f);
                yield return Bot.WaitUntil("full here too", () => full(tree(uid)), 20f);
                Bot.Step("info full here: " + GardenInfo(tree(uid)));
                Vector2 at = tree(uid).pos;
                Func<int> dropsNear = () => BeeLoot.Sum(id => Drops(id).Count(d => Vector2.Distance(d.transform.position, at) < 400f));
                int have0 = loot(), drops0 = dropsNear();
                string bag0 = Items(me.data.inventory);
                yield return WalkUpTo(tree(uid));
                Bot.Step($"info at the tree: nearest={me.components.interaction.nearest?.obj_id}");
                me.temp_do_work = true;   // hold the work key, bare hands
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (full(tree(uid)) && UnityEngine.Time.realtimeSinceStartup - t0 < 30f)
                {
                    if (me.energy < 10f) me.energy = 100f;
                    yield return null;
                }
                me.temp_do_work = false;
                Bot.Check("took the honey", !full(tree(uid)), GardenInfo(tree(uid)));
                yield return Bot.Wait(2f);
                int got = loot() - have0 + dropsNear() - drops0;
                Bot.Step($"info after: {GardenInfo(tree(uid))} bag+{loot() - have0} drops+{dropsNear() - drops0}");
                Bot.Check("got honey, bees or wax (bag or ground)", got > 0, $"[{bag0}] -> [{Items(me.data.inventory)}]");
                yield return Bot.Wait(3f);
                Bot.Check("the tree did not fill again by itself", !full(tree(uid)), GardenInfo(tree(uid)));
                Bot.Publish("taken", tree(uid)?.obj_id ?? "none");
                yield return Bot.WaitRemote(1, "taken_seen", "1", 40f);
                yield return Bot.WaitUntil("the hive fills again here too", () => tree(uid)?.components?.craft?.is_crafting == true, 10f);
                Bot.Step("info filling again: " + GardenInfo(tree(uid)));
            }
        }

        private static IEnumerator WorkOn(Func<bool> until, float seconds)
        {
            var me = MainGame.me.player;
            me.temp_do_work = true;   // holding the work key
            float t0 = UnityEngine.Time.realtimeSinceStartup;
            while (!until() && UnityEngine.Time.realtimeSinceStartup - t0 < seconds)
            {
                if (me.energy < 10f) me.energy = 100f;
                yield return null;
            }
            me.temp_do_work = false;
        }

        /// <summary>A bee house from the bee garden: the client places the blueprint at the bee
        /// table and hammers it together; the host runs the hive and fills it (fast-forwarded);
        /// the client takes the honey by hand. Both see every stage on the same object.</summary>
        private static IEnumerator BeeHouse()
        {
            var me = MainGame.me.player;
            Func<Vector2, WorldGameObject> houseAt = at => WorldMap.objs.Where(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.StartsWith("beehouse_") &&
                                                                                 w.obj_id != "beehouse_decor" && w.obj_id != "beehouse_broken" && Vector2.Distance(w.pos, at) < 48f)
                                                                     .FirstOrDefault();
            Func<WorldGameObject, string> info = w => w == null ? "none" : GardenInfo(w) + $" tools=[{string.Join(",", w.obj_def?.tool_actions?.action_tools ?? new List<ItemDefinition.ItemType>())}]";
            if (!Bot.IsHost)
            {
                var desk = WorldMap.objs.FirstOrDefault(w => w != null && !w.is_removed && w.obj_id == "beegarden_table");
                var cd = GameBalance.me.GetDataOrNull<ObjectCraftDefinition>("beegarden_table:p:beehouse_place");
                Bot.Check("the bee table and the bee house recipe", desk != null && cd != null);
                if (desk == null || cd == null) { Bot.Publish("house", "none"); yield break; }
                foreach (var n in cd.needs) me.data.AddItem(n.id, n.value);   // stands in for planks, nails and bees gathered earlier
                if (!me.data.secondary_inventory.Any(i => i?.definition?.type == ItemDefinition.ItemType.Hammer) &&
                    !me.data.inventory.Any(i => i?.definition?.type == ItemDefinition.ItemType.Hammer))
                {
                    var hammer = GameBalance.me.items_data.FirstOrDefault(i => i.type == ItemDefinition.ItemType.Hammer);
                    Bot.Step("info no hammer, adding " + hammer?.id);
                    if (hammer != null) me.data.AddItem(hammer.id, 1);
                }
                string bag0 = Items(me.data.inventory);
                yield return WalkUpTo(desk);
                MainGame.me.OpenBuildObjectGUI(desk);
                yield return Bot.Wait(1f);
                object wasGamepad = GamepadActiveF.GetValue(null);
                GamepadActiveF.SetValue(null, true);
                MainGame.me.build_mode_logics.CraftBuilding(cd);
                yield return Bot.Wait(0.5f);
                Bot.Check("placing a bee house blueprint", BuildMode() == "Placing" && FloatingWorldGameObject.cur_floating != null, BuildMode());
                var zone = WorldZone.GetZoneByID(desk.obj_def.zone_id);
                var b = zone.GetBounds();
                bool free = false;
                for (float x = b.min.x + 96f; x < b.max.x - 96f && !free; x += 96f)
                    for (float y = b.min.y + 96f; y < b.max.y - 96f && !free; y += 96f)
                    {
                        if (FloatingWorldGameObject.cur_floating == null) break;
                        FloatingWorldGameObject.MoveCurrentFloatingObject(new Vector2(x, y));
                        yield return null;
                        free = FloatingWorldGameObject.can_be_built && MainGame.me.build_mode_logics.CanBuild(cd);
                    }
                Bot.Check("a free spot in the bee garden", free);
                Vector2 at = FloatingWorldGameObject.cur_floating?.wobj != null ? (Vector2)FloatingWorldGameObject.cur_floating.wobj.pos : Vector2.zero;
                var preview = FloatingWorldGameObject.cur_floating?.wobj;
                if (free) { yield return FakeInput.Press(GameKey.Interaction); yield return Bot.Wait(1f); }
                for (int i = 0; i < 4 && (BuildMode() == "Placing" || GUIElements.me.craft.is_shown || MainGame.me.game_mode == MainGame.GameMode.Building); i++)
                {
                    yield return FakeInput.Press(GameKey.Back);
                    yield return Bot.Wait(0.6f);
                    if (GUIElements.me.craft.is_shown) { GUIElements.me.craft.OnClosePressed(); yield return Bot.Wait(0.6f); }
                }
                GamepadActiveF.SetValue(null, wasGamepad);
                var house = WorldMap.objs.FirstOrDefault(w => w != null && !w.is_removed && w != FloatingWorldGameObject.cur_floating?.wobj && w.obj_id == "beehouse_place" && Vector2.Distance(w.pos, at) < 48f);
                Bot.Check("blueprint placed", house != null, $"at {V(at)} near: " + string.Join(",", WorldMap.objs.Where(w => w != null && !w.is_removed && w != preview && w.obj_id != null && Vector2.Distance(w.pos, at) < 150f).Select(w => $"{w.obj_id}#{w.unique_id}@{V(w.pos)}")) + $" bag [{Items(me.data.inventory)}]");
                if (house == null) { Bot.Publish("house", "none"); yield break; }
                long uid = house.unique_id;
                Func<WorldGameObject> cur = () => WorldMap.GetWorldGameObjectByUniqueId(uid, false) ?? houseAt(at);
                Bot.Step("info placed: " + info(house) + $" bag [{bag0}] -> [{Items(me.data.inventory)}]");
                Bot.Publish("house", $"{uid}|{V(at)}");
                yield return Bot.WaitRemote(1, "place_seen", "1", 40f);

                // Hammer it together like a player.
                yield return WalkUpTo(cur());
                Bot.Step($"info at the blueprint: nearest={me.components.interaction.nearest?.obj_id}");
                yield return WorkOn(() => cur()?.obj_id != "beehouse_place", 60f);
                Bot.Check("built the bee house", cur()?.obj_id != "beehouse_place", info(cur()));
                yield return Bot.Wait(2f);
                Bot.Step("info built: " + info(cur()));
                Bot.Publish("built", cur()?.obj_id ?? "none");
                yield return Bot.WaitUntil("host filled the hive", () => Bot.Remote(1, "full") != null, 120f);
                string fullId = Bot.Remote(1, "full");
                yield return Bot.WaitUntil("the hive is full here too", () => cur()?.obj_id == fullId, 20f);
                Bot.Step("info full here: " + info(cur()));

                // Take the honey by hand.
                Func<int> loot = () => BeeLoot.Sum(id => me.data.GetTotalCount(id, true));
                Func<int> dropsNear = () => BeeLoot.Sum(id => Drops(id).Count(d => Vector2.Distance(d.transform.position, at) < 400f));
                int have0 = loot(), drops0 = dropsNear();
                yield return WalkUpTo(cur());
                yield return WorkOn(() => cur()?.obj_id != fullId, 30f);
                Bot.Check("took the honey", cur()?.obj_id != fullId, info(cur()));
                yield return Bot.Wait(2f);
                Bot.Step($"info after: {info(cur())} bag+{loot() - have0} drops+{dropsNear() - drops0}");
                Bot.Check("got honey or wax (bag or ground)", loot() - have0 + dropsNear() - drops0 > 0, Items(me.data.inventory));
                Bot.Publish("taken", cur()?.obj_id ?? "none");
                yield return Bot.WaitRemote(1, "taken_seen", "1", 40f);
            }
            else
            {
                yield return Bot.WaitUntil("client placed a bee house", () => Bot.Remote(2, "house") != null, 90f);
                if (Bot.Remote(2, "house") == "none") yield break;
                var p = Bot.Remote(2, "house").Split('|');
                long uid = long.Parse(p[0]); Vector2 at = ParseV(p[1]);
                Func<WorldGameObject> cur = () => WorldMap.GetWorldGameObjectByUniqueId(uid, false) ?? houseAt(at);
                yield return Bot.WaitUntil("the blueprint is here", () => cur()?.obj_id == "beehouse_place", 20f);
                Bot.Step("info blueprint here: " + info(cur()));
                Bot.Publish("place_seen", "1");
                yield return Bot.WaitUntil("client built it", () => Bot.Remote(2, "built") != null, 120f);
                string built = Bot.Remote(2, "built");
                yield return Bot.WaitUntil("the bee house is built here too", () => cur()?.obj_id == built, 20f);
                int copies = WorldMap.objs.Count(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.StartsWith("beehouse_") && Vector2.Distance(w.pos, at) < 48f);
                Bot.Check("exactly one bee house there", copies == 1, copies.ToString());
                Bot.Step("info built here: " + info(cur()));
                yield return Bot.WaitUntil("the new bee house starts making honey", () => cur()?.components?.craft?.is_crafting == true, 10f);
                // The host runs the hive: wait for it to start, then fast-forward until it is full.
                yield return Bot.WaitUntil("the bee house fills", () =>
                {
                    var w = cur();
                    if (w == null) return false;
                    if (w.obj_id == "beehouse_2") return true;
                    if (w.components?.craft != null && w.components.craft.is_crafting && w.progress < 0.999f) w.progress = 0.999f;
                    return false;
                }, 60f);
                Bot.Step("info full: " + info(cur()));
                Bot.Publish("full", cur()?.obj_id ?? "none");
                Func<int> loot = () => BeeLoot.Sum(id => me.data.GetTotalCount(id, true));
                int mine0 = loot();
                yield return Bot.WaitUntil("client took the honey", () => Bot.Remote(2, "taken") != null, 90f);
                string theirs = Bot.Remote(2, "taken");
                yield return Bot.WaitUntil("the bee house looks the same here", () => (cur()?.obj_id ?? "none") == theirs, 20f);
                yield return Bot.Wait(2f);
                Bot.Step("info after the client's harvest: " + info(cur()));
                Bot.Check("the honey went only to the client (my bag unchanged)", loot() == mine0, $"{mine0} -> {loot()}");
                yield return Bot.WaitUntil("the bee house makes honey again", () => cur()?.components?.craft?.is_crafting == true, 10f);
                Bot.Step("info working again: " + info(cur()));
                Bot.Publish("taken_seen", "1");
            }
        }

        private static readonly string[] SceneCrowd = { "npc_inquisitor", "npc_guard_3", "npc_citizen_1", "npc_citizen_2", "npc_citizen_5" };

        // The spectators are ordinary citizens spawned with the tag npc_burger_eater_N.
        private static List<WorldGameObject> BurgerEaters() =>
            WorldMap.objs.Where(w => w != null && !w.is_removed && w.custom_tag != null && w.custom_tag.StartsWith("npc_burger_eater")).ToList();

        /// <summary>What the client's NPC mirror holds for an NPC: the last position from the host.</summary>
        private static string NpcMirror(WorldGameObject w)
        {
            var sync = GraveyardKeeperCoop.Multiplayer.NpcVisualSync.Instance;
            if (sync == null || w == null) return "-";
            var states = HarmonyLib.AccessTools.Field(sync.GetType(), "targetStates").GetValue(sync) as System.Collections.IDictionary;
            var st = states?[w.unique_id];
            if (st == null) return "no mirror";
            var t = st.GetType();
            var last = (Vector3)HarmonyLib.AccessTools.Field(t, "LastReceivedPosition").GetValue(st);
            var samples = HarmonyLib.AccessTools.Field(t, "PositionSamples").GetValue(st) as System.Collections.IList;
            return $"last={V(last)} samples={samples?.Count} epoch={HarmonyLib.AccessTools.Field(t, "PuppetEpoch").GetValue(st)}";
        }

        /// <summary>"tag@x;y active" for the fixed NPCs of the burning scene.</summary>
        private static Dictionary<string, string> CrowdState() =>
            SceneCrowd.ToDictionary(t => t, t =>
            {
                var w = WorldMap.GetWorldGameObjectByCustomTag(t);
                return w == null ? "none" : $"{V(w.pos)}|{w.gameObject.activeInHierarchy}";
            });

        /// <summary>A story scene with a crowd: the weekly witch burning on the witch hill
        /// (inquisitor, a guard, three citizens and eight spawned spectators). One player
        /// starts it and clicks through it, the other watches. Both must see the scene, both
        /// get control back, the spectators leave on both sides, the fixed crowd ends up in
        /// the same places, and the reward goes to the initiator only.</summary>
        private static IEnumerator Scene(int initiator)
        {
            InstallBubbleHook();
            var me = MainGame.me.player;
            Bot.KeepAlive = true;   // the hill has night monsters too
            var pylon = WorldMap.GetWorldGameObjectByCustomTag("witch_pylon");
            Bot.Require("the witch pylon", pylon != null);
            // Stand at the foot of the hill, a little apart.
            me.PlaceAtPos((Vector2)pylon.pos + new Vector2(Plugin.Player == 1 ? -150f : 150f, -500f));
            yield return Bot.Wait(2f);
            float money0 = me.data.money;
            bubblesSeen.Clear();
            Bot.Step("info crowd before: " + string.Join(" ", CrowdState().Select(kv => kv.Key + "=" + kv.Value)) + $" pylon={pylon.obj_id}");
            yield return Bot.Sync("scene_ready");
            bool lostControl = false; int maxEaters = 0;
            float t0 = UnityEngine.Time.realtimeSinceStartup, quiet = t0, lastNote = 0f;
            Action watch = () =>
            {
                if (!CanMove()) lostControl = true;
                maxEaters = Math.Max(maxEaters, BurgerEaters().Count);
            };
            if (Plugin.Player == initiator)
            {
                var script = GS.RunFlowScript("witch_tasty_burning");
                Bot.Check("the scene started", script != null);
                int presses = 0, answersPicked = 0;
                while (UnityEngine.Time.realtimeSinceStartup - quiet < 6f && UnityEngine.Time.realtimeSinceStartup - t0 < 240f)
                {
                    watch();
                    var answers = OpenAnswers();
                    if (answers != null && answers.Count > 0)
                    {
                        yield return Bot.Wait(0.8f);
                        answers.First(a => a != null).OnChosen();
                        answersPicked++;
                        quiet = UnityEngine.Time.realtimeSinceStartup;
                        continue;
                    }
                    if (ActiveBubble() != null)
                    {
                        yield return Bot.Wait(0.7f);
                        yield return FakeInput.Press(GameKey.Select);
                        presses++;
                        quiet = UnityEngine.Time.realtimeSinceStartup;
                        continue;
                    }
                    if (!CanMove()) quiet = UnityEngine.Time.realtimeSinceStartup;
                    if (UnityEngine.Time.realtimeSinceStartup - lastNote > 10f)
                    {
                        lastNote = UnityEngine.Time.realtimeSinceStartup;
                        Bot.Step($"info scene running: canMove={CanMove()} eaters={BurgerEaters().Count} windows=[{string.Join(",", BaseGUI.opened_windows.Select(w => w.GetType().Name))}]");
                    }
                    yield return null;
                }
                Bot.Step($"info scene over after {UnityEngine.Time.realtimeSinceStartup - t0:F0}s: {bubblesSeen.Count} lines, {presses} clicks, {answersPicked} answers, lost control={lostControl}, spectators max {maxEaters}");
                foreach (var b in bubblesSeen.Take(8)) Bot.Step("     line " + b);
                Bot.Check("the scene played here (control taken)", lostControl);
                Bot.Publish("scene_done", string.Join("\u0001", bubblesSeen));
                // Vanilla: the citizens start walking home a few seconds after the scene.
                for (int i = 0; i < 6; i++)
                {
                    Bot.Step("info tail+" + i * 2 + "s: " + string.Join(" ", new[] { "npc_citizen_1", "npc_citizen_2", "npc_citizen_5" }.Select(t =>
                    {
                        var w = WorldMap.GetWorldGameObjectByCustomTag(t);
                        var mc = w?.components?.character;
                        return w == null ? t + "=none" : $"{t}@{V(w.pos)} moving={(mc != null && !mc.IsStopped)} state={mc?.movement_state} dest={(mc?.astar != null ? V(mc.astar.destination) : "-")}";
                    })));
                    yield return Bot.Wait(2f);
                }
            }
            else
            {
                while (Bot.Remote(initiator, "scene_done") == null && UnityEngine.Time.realtimeSinceStartup - t0 < 260f)
                {
                    watch();
                    if (UnityEngine.Time.realtimeSinceStartup - lastNote > 10f)
                    {
                        lastNote = UnityEngine.Time.realtimeSinceStartup;
                        Bot.Step($"info watching: canMove={CanMove()} eaters={BurgerEaters().Count} lines={bubblesSeen.Count}");
                    }
                    yield return null;
                }
                Bot.Step($"info watched: {bubblesSeen.Count} lines, lost control={lostControl}, spectators max {maxEaters}");
                Bot.Check("saw the scene (spectators or lines)", maxEaters > 0 || bubblesSeen.Count > 0);
                var theirs = (Bot.Remote(initiator, "scene_done") ?? "").Split('\u0001').Where(x => x.Length > 0).ToList();
                Func<string, string> text = l => l.Substring(l.IndexOf(": ") + 2);
                var missing = theirs.Select(text).Where(l => !bubblesSeen.Select(text).Contains(l)).ToList();
                Bot.Check("saw the same lines", missing.Count == 0, missing.Count == 0 ? "" : $"missing {missing.Count}/{theirs.Count}: " + string.Join(" | ", missing.Take(4)));
            }
            yield return Bot.Sync("scene_over", 120f);
            yield return Bot.WaitUntil("can move again after the scene", CanMove, 15f);
            yield return Bot.WaitUntil("the spectators left", () => BurgerEaters().Count(w => w.gameObject.activeInHierarchy) == 0, 30f);
            yield return Bot.Wait(5f);   // let the crowd walk back
            var crowd = CrowdState();
            Bot.Step("info crowd after: " + string.Join(" ", crowd.Select(kv => kv.Key + "=" + kv.Value)));
            if (!Bot.IsHost)
                foreach (var t in SceneCrowd)
                {
                    var w = WorldMap.GetWorldGameObjectByCustomTag(t);
                    Bot.Step($"info mirror {t}#{w?.unique_id}: {NpcMirror(w)}");
                }
            float gained = me.data.money - money0;
            Bot.Step($"info money {F(money0)} -> {F(me.data.money)}");
            if (Plugin.Player == initiator) Bot.Check("the initiator got the reward", gained > 0f, F(gained));
            else Bot.Check("the watcher got no reward", Mathf.Abs(gained) < 0.001f, F(gained));
            Func<string> crowdLine = () => string.Join(" ", CrowdState().Select(kv => kv.Key + "=" + kv.Value));
            if (Bot.IsHost)
            {
                yield return Bot.WaitUntil("partner reported the crowd", () => Bot.Remote(2, "crowd") != null, 60f);
                Func<string, Dictionary<string, string>> parse = x => x.Split(' ').Select(e => e.Split('=')).ToDictionary(a => a[0], a => a[1]);
                var off = new List<string>();
                // The client mirrors the host's NPCs a little behind; within two tiles is the same place.
                yield return Bot.WaitUntil("the crowd stands in the same places", () =>
                {
                    off.Clear();
                    string r = Bot.Remote(2, "crowd");
                    if (r == null) { off.Add("no report"); return false; }
                    var theirs = parse(r);
                    foreach (var kv in CrowdState())
                    {
                        string t; theirs.TryGetValue(kv.Key, out t);
                        if (kv.Value == "none" || t == null || t == "none") { if (kv.Value != t) off.Add($"{kv.Key} here {kv.Value} there {t}"); continue; }
                        // Only the place counts: whether an object is switched on follows each player's own camera.
                        var x = kv.Value.Split('|'); var y = t.Split('|');
                        if (Vector2.Distance(ParseV(x[0]), ParseV(y[0])) > 192f) off.Add($"{kv.Key} here {kv.Value} there {t}");
                    }
                    return off.Count == 0;
                }, 30f);
                if (off.Count > 0) Bot.Step("info differs: " + string.Join("; ", off));
                Bot.Publish("crowd_compared", "1");
            }
            else
            {
                // Keep reporting until the host has compared: the crowd is still walking.
                float c0 = UnityEngine.Time.realtimeSinceStartup;
                while (Bot.Remote(1, "crowd_compared") == null && UnityEngine.Time.realtimeSinceStartup - c0 < 120f)
                {
                    Bot.Publish("crowd", crowdLine());
                    yield return Bot.Wait(1f);
                }
            }
            yield return Bot.Sync("scene_end");
            if (!Bot.IsHost)
                foreach (var t in SceneCrowd)
                {
                    var w = WorldMap.GetWorldGameObjectByCustomTag(t);
                    Bot.Step($"info later {t}: {(w == null ? "none" : V(w.pos) + " active=" + w.gameObject.activeInHierarchy)} mirror {NpcMirror(w)}");
                }
        }

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
                    yield return ReachExit(exit);
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

        internal static SpeechBubbleGUI ActiveBubble() =>
            SpeechBubbleGUI.all.Values.FirstOrDefault(b => b != null && b.gameObject.activeInHierarchy &&
                                                           !(bool)HarmonyLib.AccessTools.Field(typeof(SpeechBubbleGUI), "_disappearing").GetValue(b));

        internal static List<MultiAnswerOptionGUI> OpenAnswers()
        {
            var cur = HarmonyLib.AccessTools.Field(typeof(MultiAnswerGUI), "_current").GetValue(null) as MultiAnswerGUI;
            if (cur == null || !cur.gameObject.activeInHierarchy) return null;
            var list = HarmonyLib.AccessTools.Field(typeof(MultiAnswerGUI), "_answers").GetValue(cur) as List<MultiAnswerOptionGUI>;
            return list != null && list.Count > 0 && list.Any(a => a != null) ? list : null;
        }

        internal static bool CanMove()
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
        /// <summary>Every vendor's money and stock, in uid order.</summary>
        private static Dictionary<string, string> VendorsByUid(string digest) =>
            System.Text.RegularExpressions.Regex.Matches(digest, @"(\d+):(money=\S+ stock=\[[^\]]*\])")
                .Cast<System.Text.RegularExpressions.Match>()
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        private static string VendorsDigest() => string.Join(" ", WorldMap.objs
            .Where(o => o != null && !o.is_removed && o.vendor != null)
            .OrderBy(o => o.unique_id)
            .Select(o => o.unique_id + ":" + VendorState(o)).ToArray());

        /// <summary>The end of a day: vendors earn their income and restock (random amounts).
        /// It runs once, on the host, and both sides then see the same vendors.</summary>
        private static IEnumerator VendorDayEnd()
        {
            if (Bot.IsHost)
            {
                string before = VendorsDigest();
                Bot.Publish("vendors_before", before);
                yield return Bot.WaitRemote(2, "vendors_before_seen", "1", 30f);
                WorldMap.VendorsTradeWithBank();   // what the game does at the end of the day
                string after = VendorsDigest();
                Bot.Check("the vendors traded with the bank", after != before, "no vendor changed");
                Bot.Publish("vendors", after);
                yield return Bot.WaitRemote(2, "vendors_same", "1", 60f);
            }
            else
            {
                yield return Bot.WaitUntil("host's vendors before", () => Bot.Remote(1, "vendors_before") != null, 30f);
                string before = VendorsDigest();
                {
                    var host0 = VendorsByUid(Bot.Remote(1, "vendors_before"));
                    var mine0 = VendorsByUid(before);
                    var diff0 = host0.Where(kv => !mine0.ContainsKey(kv.Key) || mine0[kv.Key] != kv.Value).ToList();
                    Bot.Step($"info before the day end {diff0.Count} of {host0.Count} vendors differ: " + string.Join(" | ",
                        diff0.Select(kv => kv.Key + " here " + (mine0.TryGetValue(kv.Key, out var m0) ? m0 : "missing") + " host " + kv.Value).ToArray()));
                }
                Bot.Publish("vendors_before_seen", "1");
                yield return Bot.WaitUntil("host's day ended", () => Bot.Remote(1, "vendors") != null, 60f);
                string theirs = Bot.Remote(1, "vendors");
                yield return Bot.WaitUntil("the same vendors here", () => VendorsDigest() == theirs, 30f);
                if (VendorsDigest() != theirs)
                {
                    var host = VendorsByUid(theirs);
                    var mine = VendorsByUid(VendorsDigest());
                    Bot.Step($"info {host.Count(kv => !mine.ContainsKey(kv.Key) || mine[kv.Key] != kv.Value)} of {host.Count} vendors differ: " + string.Join(" | ",
                        host.Where(kv => !mine.ContainsKey(kv.Key) || mine[kv.Key] != kv.Value)
                            .Select(kv => kv.Key + " here " + (mine.TryGetValue(kv.Key, out var m) ? m : "missing") + " host " + kv.Value).ToArray()));
                }
                // The client's own end of day must not run: it would restock with other numbers.
                WorldMap.VendorsTradeWithBank();
                yield return Bot.Wait(1f);
                Bot.Check("my end of day leaves the vendors alone", VendorsDigest() == theirs, "changed after a local end of day");
                Bot.Check("the vendors changed", theirs != before, "same as before");
                Bot.Publish("vendors_same", "1");
            }
        }

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
                    yield return ReachExit(exit);
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
                    yield return ReachExit(exit);
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
                    yield return ReachExit(exit);
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
        /// <summary>Walk up to the dungeon exit until it is the thing the action key uses: a
        /// bat hovering next to it can be nearer, so step back and try again a few times.</summary>
        private static IEnumerator ReachExit(WorldGameObject exit)
        {
            var inter = MainGame.me.player.components.interaction;
            float[] distances = { 120f, 60f, 30f };
            for (int attempt = 0; attempt < 9; attempt++)
            {
                yield return WalkUpTo(exit, distances[attempt % distances.Length]);
                if (inter.nearest == exit) yield break;
                var me = MainGame.me.player;
                var blocker = inter.nearest;
                Bot.Step($"info exit blocked by {blocker?.obj_id}; trying again " +
                         $"me={me.pos.x:0};{me.pos.y:0} exit={exit.pos.x:0};{exit.pos.y:0} " +
                         $"enabled={GS.IsPlayerEnable()} inter={inter.enabled} " +
                         $"blocker={blocker?.unique_id}@{(blocker != null ? blocker.pos.x.ToString("0") + ";" + blocker.pos.y.ToString("0") : "-")} " +
                         $"inDungeonRoot={(blocker != null && MainGame.me.dungeon_root != null && blocker.transform.IsChildOf(MainGame.me.dungeon_root.transform))}");
                // A wandering dungeon monster stuck in the exit doorway (its pathfinding
                // gives up there) blocks the walk and takes the interaction. The test is
                // about leaving, not fighting: move it out of the way.
                if (blocker != null && blocker != exit && blocker.obj_def != null &&
                    blocker.obj_def.type == ObjectDefinition.ObjType.Mob)
                {
                    Vector2 away = ((Vector2)blocker.pos - (Vector2)exit.pos);
                    if (away.sqrMagnitude < 1f) away = Vector2.right;
                    blocker.PlaceAtPos((Vector2)exit.pos + away.normalized * 400f);
                }
                yield return Bot.Wait(1.5f);
            }
        }

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
