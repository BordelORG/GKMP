using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CoopQA
{
    /// <summary>
    /// Single player, works with or without the mod: loads save slot QA_SAVE, stands at the
    /// witch hill and plays the weekly burning scene, clicking through it like a player.
    /// Tells whether the scene finishes in this save at all. Result: QA_OUT/scene_&lt;label&gt;.txt
    /// </summary>
    internal static class SoloScene
    {
        public static IEnumerator Run(string outDir, string slotName, string label, string scriptName)
        {
            var log = new System.Text.StringBuilder();
            Action<string> L = s => { log.AppendLine(s); Plugin.Log.LogInfo("[SoloScene] " + s); };
            float t0 = Time.realtimeSinceStartup;
            while (!(GUIElements.me != null && GUIElements.me.main_menu != null && GUIElements.me.main_menu.is_shown && !LoadingGUI.is_shown))
            {
                if (Time.realtimeSinceStartup - t0 > 240f) { L("timeout waiting for main menu"); goto done; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(2f);
            {
                System.Collections.Generic.List<SaveSlotData> slots = null;
                PlatformSpecific.ReadSaveSlots(s => slots = s);
                while (slots == null) yield return null;
                var slot = slots.FirstOrDefault(s => s != null && s.filename_no_extension == slotName);
                if (slot == null) { L("no save " + slotName); goto done; }
                GUIElements.me.saves.OnSelectSlotPressed(slot);
            }
            t0 = Time.realtimeSinceStartup;
            while (!(MainGame.game_started && MainGame.me?.player != null && !LoadingGUI.is_shown))
            {
                if (Time.realtimeSinceStartup - t0 > 300f) { L("timeout waiting for the game"); goto done; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(5f);
            {
                var me = MainGame.me.player;
                var pylon = WorldMap.GetWorldGameObjectByCustomTag("witch_pylon");
                L($"day={MainGame.me.save.day} pylon={pylon?.obj_id} money={me.data.money}");
                if (pylon != null) me.PlaceAtPos((Vector2)pylon.pos + new Vector2(-150f, -500f));
                yield return new WaitForSecondsRealtime(2f);
                float money0 = me.data.money;
                var script = GS.RunFlowScript(scriptName);
                L($"started {scriptName}: {script != null}");
                bool lost = false;
                int lines = 0, answers = 0;
                float start = Time.realtimeSinceStartup, quiet = start, note = 0f;
                while (Time.realtimeSinceStartup - quiet < 8f && Time.realtimeSinceStartup - start < 150f)
                {
                    bool canMove = SyncTests.CanMove();
                    if (!canMove) { lost = true; quiet = Time.realtimeSinceStartup; }
                    var ans = SyncTests.OpenAnswers();
                    if (ans != null) { yield return new WaitForSecondsRealtime(0.8f); ans.First(a => a != null).OnChosen(); answers++; quiet = Time.realtimeSinceStartup; continue; }
                    if (SyncTests.ActiveBubble() != null) { yield return new WaitForSecondsRealtime(0.7f); yield return FakeInput.Press(GameKey.Select); lines++; quiet = Time.realtimeSinceStartup; continue; }
                    if (Time.realtimeSinceStartup - note > 5f)
                    {
                        note = Time.realtimeSinceStartup;
                        var inq = WorldMap.GetWorldGameObjectByCustomTag("npc_inquisitor");
                        L($"  t={Time.realtimeSinceStartup - start:F0}s canMove={canMove} control={MainGame.me.player_char.control_enabled} inquisitor@{inq?.pos} eaters={WorldMap.objs.Count(w => w != null && !w.is_removed && w.obj_id != null && w.obj_id.StartsWith("npc_burger_eater"))}");
                    }
                    yield return null;
                }
                L($"finished={!(Time.realtimeSinceStartup - start >= 150f)} after {Time.realtimeSinceStartup - start:F0}s lost_control={lost} clicks={lines} answers={answers} canMove={SyncTests.CanMove()} money {money0} -> {me.data.money}");
                // What the crowd does after the scene: walks home on its own, or stays.
                for (int i = 0; i < 5; i++)
                {
                    L("  after+" + (i * 5) + "s: " + string.Join(" ", new[] { "npc_citizen_1", "npc_citizen_2", "npc_citizen_5", "npc_guard_3" }.Select(t =>
                    {
                        var w = WorldMap.GetWorldGameObjectByCustomTag(t);
                        var mc = w?.components?.character;
                        return w == null ? t + "=none" : $"{t}@{(Vector2)w.pos} active={w.gameObject.activeInHierarchy} moving={(mc != null && !mc.IsStopped)} dest={(mc?.astar != null ? (Vector2)mc.astar.destination : Vector2.zero)}";
                    })) + " scripts=" + string.Join(",", UnityEngine.Object.FindObjectsOfType<CustomFlowScript>().Select(c => c.script_name).Where(n => n != null && n.Contains("witch"))));
                    yield return new WaitForSecondsRealtime(5f);
                }
            }
        done:
            try { File.WriteAllText(Path.Combine(outDir, $"scene_{label}.txt"), log.ToString()); } catch { }
            L("DONE");
        }
    }
}
