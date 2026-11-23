using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CoopQA
{
    /// <summary>
    /// Single-player probe that touches only game types, so it runs with or without the mod:
    /// loads save slot QA_SAVE from the main menu (like clicking it), then reports the HUD
    /// toolbar state and writes a screenshot. Result: QA_OUT/hud_&lt;label&gt;.txt/.png
    /// </summary>
    internal static class HudProbe
    {
        public static IEnumerator Run(string outDir, string slotName, string label)
        {
            var log = new System.Text.StringBuilder();
            Action<string> L = s => { log.AppendLine(s); Plugin.Log.LogInfo("[HudProbe] " + s); };
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
                L($"slots: {string.Join(",", slots.Where(s => s != null).Select(s => s.filename_no_extension))}; using '{slotName}' found={slot != null}");
                if (slot == null) goto done;
                GUIElements.me.saves.OnSelectSlotPressed(slot);   // clicking the save
            }
            t0 = Time.realtimeSinceStartup;
            while (!(MainGame.game_started && MainGame.me?.player != null && !LoadingGUI.is_shown))
            {
                if (Time.realtimeSinceStartup - t0 > 300f) { L("timeout waiting for the game"); goto done; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(8f);
            {
                var hud = GUIElements.me.hud;
                var tb = hud?.toolbar;
                L($"hud_enabled={GUIElements.me.hud_enabled} hud active={hud?.gameObject.activeInHierarchy}");
                L($"toolbar activeSelf={tb?.gameObject.activeSelf} activeInHierarchy={tb?.gameObject.activeInHierarchy}");
                L($"keyboard active={tb?.keyboard?.gameObject.activeInHierarchy} gamepad active={tb?.gamepad?.gameObject.activeInHierarchy} gamepad_input={LazyInput.gamepad_active}");
                L("equipped_items=[" + string.Join(",", MainGame.me.save.equipped_items ?? new string[0]) + "]");
                if (tb?.keyboard != null)
                {
                    foreach (var w in tb.keyboard.GetComponentsInChildren<UIWidget>(true).Take(30))
                        L($"  widget {w.name} active={w.gameObject.activeInHierarchy} enabled={w.enabled} alpha={w.finalAlpha:F2}");
                    foreach (var p in tb.GetComponentsInParent<UIPanel>(true))
                        L($"  panel {p.name} alpha={p.alpha:F2}");
                }
                string png = Path.Combine(outDir, $"hud_{label}.png");
                ScreenCapture.CaptureScreenshot(png);
                L("screenshot " + png);
            }
            yield return new WaitForSecondsRealtime(2f);
        done:
            try { File.WriteAllText(Path.Combine(outDir, $"hud_{label}.txt"), log.ToString()); } catch { }
            L("DONE");
        }
    }
}
