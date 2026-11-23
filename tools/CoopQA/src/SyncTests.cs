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
        };

        private static int Other => Bot.IsHost ? 2 : 1;
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
                Vector3 spot = MainGame.me.player.transform.position + new Vector3(250f, 0f, 0f);
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
                if (docks != null && docks.Length > 0)
                {
                    var dp = docks.Where(x => x != null).OrderBy(x => ((Vector2)x.transform.position - me.pos).sqrMagnitude).FirstOrDefault();
                    if (dp != null)
                    {
                        me.PlaceAtPos(dp.transform.position);
                        yield return Bot.Wait(0.4f);
                        Bot.Step($"info stood on a dock point ({docks.Length} total); nearest={inter.nearest?.obj_id}");
                    }
                }
                Bot.Step($"info nearest after walking up: {inter.nearest?.obj_id}#{inter.nearest?.unique_id} tool={me.GetEquippedTool()?.id}");
                Bot.Step("info before start: " + string.Join(",", craft.needs.Select(n => $"{n.id} {me.data.GetTotalCount(n.id, true)}/{n.value}")) +
                         $" craft tools=[{string.Join(",", craft.tool_actions.action_tools)}] station tools=[{string.Join(",", station.obj_def.tool_actions.action_tools)}]" +
                         $" belt=[{string.Join(",", me.data.secondary_inventory.Where(i => i != null && !i.IsEmpty()).Select(i => i.id + ":" + i.definition?.type))}]");
                bool started = start != null ? start(station, craft) : cc.CraftAsPlayer(craft);   // what clicking the recipe does
                me.temp_do_work = true;   // holding the work key
                Bot.Publish("craft", $"{station.unique_id}|{craft.id}|{string.Join(";", outIds)}");
                // A client first gets the station lease from the host; the craft starts after that.
                float s0 = UnityEngine.Time.realtimeSinceStartup;
                yield return Bot.WaitUntil("craft running", () => cc.is_crafting, 8f);
                Bot.Step($"info craft started={started} after {UnityEngine.Time.realtimeSinceStartup - s0:F2}s");
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
                if (outIds.Count > 0)
                    Bot.Check("crafted item received (inventory or ground)", got > 0, $"+{got}");
                Bot.Step($"info station before [{stationBefore}] after [{StationState(station)}] params: {station.data.GetParam("quality", -1f)}");
                Bot.Publish("craft_changed", StationState(station) != stationBefore || got > 0 ? "1" : "0");
                Bot.Publish("craft_done", StationState(station));
                yield return Bot.WaitRemote(Other, "craft_seen", "1", 40f);
            }
            else
            {
                yield return Bot.WaitUntil("crafter started", () => Bot.Remote(crafter, "craft") != null, 40f);
                if (Bot.Remote(crafter, "craft") == "none") yield break;
                var p = Bot.Remote(crafter, "craft").Split('|');
                var station = WorldMap.GetWorldGameObjectByUniqueId(long.Parse(p[0]), false);
                Bot.Check("same station exists here", station?.components?.craft != null);
                if (station?.components?.craft == null) { Bot.Publish("craft_seen", "1"); yield break; }
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
                yield return Bot.WaitUntil("station contents match the crafter's", () => StationState(station) == theirs, 10f);
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
                Bot.Check("same day as the host", after["day"] == Bot.Remote(1, "host_day"), $"{after["day"]} vs {Bot.Remote(1, "host_day")}");
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
                Bot.Publish("host_day", MainGame.me.save.day.ToString());
                Bot.Require("invite sent again", SteamLobbyManager.Instance.InviteFriend(new Steamworks.CSteamID(Bot.IdOf(2))));
                yield return Bot.WaitUntil("friend rejoined", () => Bot.Remote(2, "rejoined") != null, 300f);
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
            string mine = MyWorld();
            Bot.Publish("resume_world", mine);
            yield return Bot.WaitUntil("other side reported its world", () => Bot.Remote(Other, "resume_world") != null, 30f);
            Bot.Check("both sides load the same stumps", Bot.Remote(Other, "resume_world") == mine,
                      $"here [{mine}] there [{Bot.Remote(Other, "resume_world")}]");
        }

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
            yield return Bot.WaitUntil("can move again after the conversation", CanMove, 5f);
            var ds = GraveyardKeeperCoop.Multiplayer.DialogueSync.Instance;
            Bot.Check("not stuck in a synced dialogue", ds == null || !ds.IsInSyncedDialogue);
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
                var me = MainGame.me.player;
                var sword = me.GetEquippedWeapon();
                Bot.Step($"info weapon={sword?.id} hp={F(me.hp)} energy={F(me.energy)}");
                float lastLog = 0f;
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                while (!Gone(uid, id) && UnityEngine.Time.realtimeSinceStartup - t0 < 60f)
                {
                    m = WorldMap.GetWorldGameObjectByUniqueId(uid, false);
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
