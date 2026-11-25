using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GraveyardKeeperCoop.Network;
using GraveyardKeeperCoop.Patches;
using GraveyardKeeperCoop.UI;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace CoopQA
{
    /// <summary>
    /// In-game test bot. Drives the game through the same methods the menu buttons call,
    /// waits with timeouts, asserts, and writes RESULT: PASS/FAIL to QA_OUT/result_P{n}.txt.
    /// Step lists per test live in Tests.cs.
    /// </summary>
    internal static partial class Bot
    {
        private static Plugin host;
        private static StreamWriter result;
        private static bool finished;
        private static readonly Dictionary<string, string> remote = new Dictionary<string, string>();
        internal static ulong LastInviteLobby;
        private static float started;

        public static bool IsHost => Plugin.Player == 1;
        public static ulong IdOf(int player) => 76561197960265728UL + 900000000UL + (ulong)player;

        public static void Start(Plugin p)
        {
            host = p;
            started = Time.realtimeSinceStartup;
            try
            {
                Directory.CreateDirectory(Plugin.OutDir);
                result = new StreamWriter(Path.Combine(Plugin.OutDir, $"result_P{Plugin.Player}.txt"), false) { AutoFlush = true };
            }
            catch (Exception e) { Plugin.Log.LogError("[CoopQA] cannot open result file: " + e.Message); }
            Line($"START test={Plugin.Test} player=P{Plugin.Player}");
            var steps = Tests.Get(Plugin.Test);
            if (steps == null) { Fail("unknown test " + Plugin.Test); return; }
            p.StartCoroutine(Run(steps));
        }

        public static void Tick() { }

        private static IEnumerator Run(IEnumerator steps)
        {
            // Manual MoveNext so an exception in a step fails the test instead of silently stopping.
            var stack = new Stack<IEnumerator>();
            stack.Push(steps);
            while (stack.Count > 0 && !finished)
            {
                object current;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    Fail("exception: " + e);
                    yield break;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            if (!finished)
            {
                if (softFailures.Count > 0) Fail($"{softFailures.Count} check(s) failed: " + string.Join("; ", softFailures));
                else Pass();
            }
        }

        // ------------------------------------------------------------------ assertions / waits
        /// <summary>While true (session setup), a timeout ends the run; afterwards timeouts are soft.</summary>
        public static bool HardWaits = true;

        /// <summary>While set, the local player is healed whenever its health drops low.</summary>
        public static bool KeepAlive;

        /// <summary>Net hiccups for the rest of the run (host only): every <see cref="HiccupPeriod"/>
        /// seconds the P2P link goes down for <see cref="HiccupMs"/> ms.</summary>
        public static float HiccupPeriod;
        public static int HiccupMs;
        public static int Hiccups;
        private static float nextHiccup;

        public static void TickHiccups()
        {
            if (HiccupPeriod <= 0f || !IsHost || Plugin.Broker == null) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (nextHiccup <= 0f) nextHiccup = now + HiccupPeriod;
            if (now < nextHiccup) return;
            nextHiccup = now + HiccupPeriod;
            Hiccups++;
            Plugin.Broker.Send(new { op = "blackout", ms = HiccupMs });
            Step($"info net hiccup #{Hiccups}: link down {HiccupMs} ms");
        }

        public static IEnumerator WaitUntil(string what, Func<bool> cond, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                bool ok;
                try { ok = cond(); } catch (Exception e) { ok = false; Plugin.Log.LogDebug($"[CoopQA] wait '{what}': {e.Message}"); }
                if (ok) { Step($"ok   {what} ({Time.realtimeSinceStartup - t0:F1}s)"); yield break; }
                if (Time.realtimeSinceStartup - t0 > timeout && HardWaits) { Fail($"timeout {timeout:F0}s waiting for: {what}"); yield break; }
                if (Time.realtimeSinceStartup - t0 > timeout)
                {
                    Step($"FAIL timeout {timeout:F0}s waiting for: {what}");
                    softFailures.Add("timeout: " + what);
                    yield break;
                }
                yield return null;
            }
        }

        public static IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        private static readonly List<string> softFailures = new List<string>();

        /// <summary>Soft assertion: records a failure and keeps going so one run reports every problem.</summary>
        public static void Check(string what, bool cond, string detail = "")
        {
            if (cond) Step($"ok   {what} {detail}");
            else { Step($"FAIL {what} {detail}"); softFailures.Add(what); }
        }

        /// <summary>Hard assertion for preconditions the next steps depend on.</summary>
        public static void Require(string what, bool cond, string detail = "")
        {
            if (cond) Step($"ok   {what} {detail}");
            else Fail($"precondition failed: {what} {detail}");
        }

        public static void Step(string s) => Line("STEP " + s);

        private static void Line(string s)
        {
            string stamped = $"[{Time.realtimeSinceStartup - started,7:F1}s] {s}";
            Plugin.Log.LogInfo("[CoopQA] " + stamped);
            try { result?.WriteLine(stamped); } catch { }
        }

        public static void Fail(string why)
        {
            if (finished) return;
            finished = true;
            Line("RESULT: FAIL " + why);
            Publish("result", "FAIL");
            if (host != null) host.StopAllCoroutines();
        }

        private static void Pass()
        {
            finished = true;
            Line("RESULT: PASS");
            Publish("result", "PASS");
        }

        // ------------------------------------------------------------------ side channel between bots
        /// <summary>Current test name; test data keys are namespaced by it so a later test never
        /// reads a value an earlier test published under the same key.</summary>
        public static string Scope = "";

        private static string Scoped(string k) =>
            k == "result" || k == "state" || Scope.Length == 0 ? k : Scope + "/" + k;

        public static void Publish(string k, string v)
        {
            if (Plugin.Broker != null) Plugin.Broker.Send(new { op = "qa", k = Scoped(k), v });
        }

        public static void OnSideChannel(int from, string k, string v)
        {
            remote[$"P{from}.{k}"] = v;
            if (k == "result" && v == "FAIL" && !finished) Fail($"P{from} stopped with a failure");
        }

        /// <summary>Read a value another test published (explicit scope).</summary>
        public static string RemoteIn(int player, string scope, string k) => remote.TryGetValue($"P{player}.{scope}/{k}", out var v) ? v : null;

        public static string Remote(int player, string k) => remote.TryGetValue($"P{player}.{Scoped(k)}", out var v) ? v : null;

        public static IEnumerator WaitRemote(int player, string k, string v, float timeout) =>
            WaitUntil($"P{player} {k}={v}", () => Remote(player, k) == v, timeout);

        /// <summary>Barrier: both bots publish a marker and wait for the other's.</summary>
        public static IEnumerator Sync(string marker, float timeout = 120f)
        {
            // One key per marker: a single "sync" key gets overwritten by the next barrier
            // before the slower bot reads it.
            Publish("sync." + marker, "1");
            int other = IsHost ? 2 : 1;
            yield return WaitRemote(other, "sync." + marker, "1", timeout);
        }

        // ------------------------------------------------------------------ reflection helpers
        public static object Call(object target, string method, params object[] args)
        {
            Type t = target as Type ?? target.GetType();
            var m = AccessTools.Method(t, method, args.Select(a => a?.GetType() ?? typeof(object)).ToArray())
                    ?? AccessTools.Method(t, method)
                    ?? throw new MissingMethodException(t.FullName, method);
            return m.Invoke(target is Type ? null : target, args);
        }

        public static T Get<T>(object target, string field)
        {
            Type t = target as Type ?? target.GetType();
            var f = AccessTools.Field(t, field) ?? throw new MissingFieldException(t.FullName, field);
            return (T)f.GetValue(target is Type ? null : target);
        }
    }
}
