using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GraveyardKeeperCoop.Multiplayer;
using GraveyardKeeperCoop.Network;
using GraveyardKeeperCoop.Patches;
using GraveyardKeeperCoop.UI;
using Steamworks;
using UnityEngine;

namespace CoopQA
{
    internal static partial class Bot
    {
        /// <summary>Host save to load (copied into the host test copy by run_qa.py).</summary>
        public static string FixtureSave => System.Environment.GetEnvironmentVariable("QA_SAVE") ?? "coop_1";
    }

    internal static class Tests
    {
        public static IEnumerator Get(string name)
        {
            switch (name)
            {
                case "join": return Join(holdSeconds: 20f);
                case "suite": return Suite(null);
                default:
                    // suite:a,b,c runs only the listed sync tests (after joining)
                    if (name.StartsWith("suite:")) return Suite(name.Substring(6).Split(',').ToList());
                    return null;
            }
        }

        // ================================================================== session setup
        private static bool MainMenuUp() =>
            GUIElements.me != null && GUIElements.me.main_menu != null && GUIElements.me.main_menu.is_shown && !LoadingGUI.is_shown;

        internal static bool InSession()
        {
            var ocm = OnlineCoopManager.Instance;
            return MainGame.game_started && MainGame.me?.player != null && ocm != null && ocm.IsOnlineCoopEnabled &&
                   ocm.RemotePlayerCount >= 1 && !LoadingGUI.is_shown &&
                   (GameLoadSync.Instance == null || !GameLoadSync.Instance.IsWaitingForSync);
        }

        public static IEnumerator Join(float holdSeconds)
        {
            yield return Bot.WaitUntil("main menu shown", MainMenuUp, 240f);
            yield return Bot.Wait(2f);
            Bot.Publish("state", "menu");

            if (Bot.IsHost)
            {
                MainMenuPatches.OnMultiplayerButtonPressed(GUIElements.me.main_menu);
                yield return Bot.WaitUntil("multiplayer submenu", () => MultiplayerSubMenuGUI.Instance != null, 10f);
                Bot.Call(MultiplayerSubMenuGUI.Instance, "OnHostPressed");
                yield return Bot.WaitUntil("host lobby created", () => SteamLobbyManager.Instance.IsInLobby && SteamLobbyManager.Instance.IsHost, 30f);
                yield return Bot.WaitRemote(2, "state", "menu", 240f);
                Bot.Require("invite sent", SteamLobbyManager.Instance.InviteFriend(new CSteamID(Bot.IdOf(2))));
                yield return Bot.WaitUntil("client in lobby", () => SteamLobbyManager.Instance.GetLobbyMemberCount() == 2, 60f);

                // Pick the fixture save the same way a click on the slot does.
                List<SaveSlotData> slots = null;
                PlatformSpecific.ReadSaveSlots(s => slots = s);
                yield return Bot.WaitUntil("save slots read", () => slots != null, 20f);
                var slot = slots.FirstOrDefault(s => s != null && s.filename_no_extension == Bot.FixtureSave);
                Bot.Require($"fixture save '{Bot.FixtureSave}' present", slot != null, "have: " + string.Join(",", slots.Select(s => s?.filename_no_extension)));
                var panel = Bot.Get<SaveSelectorPanel>(LobbyGUI.Instance, "saveSelectorPanel");
                Bot.Call(panel, "OnSlotClicked", slot);
                Bot.Require("save selected", panel.HasSelection() && panel.GetSelectedSlot()?.filename_no_extension == Bot.FixtureSave);

                yield return Bot.WaitUntil("client ready", () => LobbyReadySystem.IsPlayerReady(new CSteamID(Bot.IdOf(2))), 60f);
                Bot.Call(LobbyGUI.Instance, "OnReadyPressed");
                yield return Bot.WaitUntil("all ready", LobbyReadySystem.CheckAllPlayersReady, 15f);
                Bot.Call(LobbyGUI.Instance, "OnStartPressed");
            }
            else
            {
                yield return Bot.WaitUntil("invite received", () => Bot.LastInviteLobby != 0, 240f);
                yield return Bot.Wait(1.5f);
                Bot.Call(SteamLobbyManager.Instance, "AcceptInvite", new CSteamID(Bot.LastInviteLobby), "QA-P1");
                yield return Bot.WaitUntil("joined host lobby", () => SteamLobbyManager.Instance.IsInLobby && !SteamLobbyManager.Instance.IsHost, 60f);
                yield return Bot.WaitUntil("lobby screen open", () => LobbyGUI.Instance != null && LobbyGUI.Instance.is_shown, 30f);
                yield return Bot.Wait(2f);
                Bot.Call(LobbyGUI.Instance, "OnReadyPressed");
                yield return Bot.WaitUntil("local ready", LobbyReadySystem.IsLocalPlayerReady, 10f);
            }

            yield return Bot.WaitUntil("both in game, coop session active", InSession, 420f);
            Bot.Publish("state", "ingame");
            yield return Bot.WaitRemote(Bot.IsHost ? 2 : 1, "state", "ingame", 120f);

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < holdSeconds)
            {
                if (!InSession()) { Bot.Fail("session dropped while holding"); yield break; }
                yield return null;
            }
            Bot.Step($"ok   session held {holdSeconds:F0}s; P2P sent={SteamMock.PacketsSent} recv={SteamMock.PacketsReceived} " +
                     $"({SteamMock.BytesSent / 1024}KB/{SteamMock.BytesReceived / 1024}KB)");
        }

        /// <summary>Close popups a test left open (tech unlock, chest, ...), like pressing close.</summary>
        private static void CloseLeftoverWindows()
        {
            foreach (var gui in BaseGUI.opened_windows.ToList())
            {
                if (gui == null || gui == GUIElements.me.sleep_gui || gui == GUIElements.me.main_menu) continue;
                try { gui.Hide(); Bot.Step("     closed leftover window " + gui.GetType().Name); }
                catch (System.Exception e) { Bot.Step($"     could not close {gui.GetType().Name}: {e.Message}"); }
            }
        }

        private static IEnumerator Suite(List<string> only)
        {
            yield return Join(holdSeconds: 5f);
            Bot.HardWaits = false;
            var known = SyncTests.All.Concat(SyncTests.Extra).ToList();
            // A filter runs the named tests in the order given; no filter runs All in order.
            var run = only == null ? SyncTests.All
                                   : only.Select(n => known.FirstOrDefault(k => k.Key == n)).Where(k => k.Key != null).ToList();
            foreach (var n in only ?? new List<string>())
                if (!known.Any(k => k.Key == n)) Bot.Step("FAIL unknown test " + n);
            foreach (var t in run)
            {
                Bot.Step("---- " + t.Key);
                Bot.Scope = t.Key;
                yield return Bot.Sync("begin:" + t.Key);
                yield return t.Value();
                Bot.KeepAlive = false;
                Bot.Step("     " + Diag.Summary());
                CloseLeftoverWindows();
                yield return Bot.Sync("end:" + t.Key, 180f);
            }
        }
    }
}
