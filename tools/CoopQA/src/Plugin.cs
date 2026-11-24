using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace CoopQA
{
    // GUID sorts before the mod's, so the Steam mock is in place before the mod registers callbacks.
    [BepInPlugin("aaa.coopqa", "CoopQA test harness", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static int Player;
        internal static string Test;
        internal static string OutDir;
        internal static BrokerClient Broker;
        internal static Plugin Instance;

        private void Awake()
        {
            Log = Logger;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-coopqa") < 0)
            {
                Log.LogInfo("[CoopQA] -coopqa not on the command line; harness disabled");
                return;
            }
            Instance = this;
            if (Environment.GetEnvironmentVariable("QA_TEST") == "hud_probe")
            {
                // Single player, no broker, no Steam mock: works without the mod installed.
                UnityEngine.Application.runInBackground = true;
                StartCoroutine(HudProbe.Run(Env("QA_OUT", Paths.GameRootPath), Env("QA_SAVE", "1"), Env("QA_LABEL", "probe")));
                return;
            }
            try
            {
                Player = int.Parse(Env("QA_PLAYER", "1"));
                Test = Env("QA_TEST", "join");
                OutDir = Env("QA_OUT", Paths.GameRootPath);
                int port = int.Parse(Env("QA_BROKER_PORT", "27800"));
                Broker = new BrokerClient("127.0.0.1", port);
                Broker.Send(new { op = "hello", player = Player, name = "QA-P" + Player });
                var welcome = Broker.WaitFor("welcome", 5000) ?? throw new Exception("broker did not answer hello");
                var h = new Harmony("aaa.coopqa");
                SteamMock.Install(h, Broker, (ulong)welcome["id"], (string)welcome["name"]);
                Diag.Install(h);
                FakeInput.Install(h);
                Bot.Start(this);
            }
            catch (Exception e)
            {
                Bot.Fail("setup: " + e);
            }
        }

        private static string Env(string k, string d) => Environment.GetEnvironmentVariable(k) is string v && v.Length > 0 ? v : d;

        private void Update()
        {
            if (Broker == null) return;
            // Two windows on one desktop: only one has focus, and Unity pauses the other by default.
            if (!UnityEngine.Application.runInBackground) UnityEngine.Application.runInBackground = true;
            // Hidden/unfocused windows get no compositor frame callbacks; with vsync the game
            // then blocks on present and falls to ~1 fps. Run unsynced, capped at 60.
            if (UnityEngine.QualitySettings.vSyncCount != 0) UnityEngine.QualitySettings.vSyncCount = 0;
            if (UnityEngine.Application.targetFrameRate != 60) UnityEngine.Application.targetFrameRate = 60;
            SteamMock.Pump();
            // Tests among monsters: the bot does not dodge, so keep its player alive.
            if (Bot.KeepAlive && MainGame.me?.player != null && !MainGame.me.player.is_dead && MainGame.me.player.hp < 60f)
                MainGame.me.player.hp = 100f;
            Bot.Tick();
        }
    }
}
