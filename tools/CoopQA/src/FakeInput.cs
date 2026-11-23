using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace CoopQA
{
    /// <summary>
    /// Presses a game key the way a keyboard does: for one frame LazyInput reports it as
    /// pressed (GetKeyDown) and held (GetKey). If the game clears input that frame, the
    /// press is gone, as with a real key.
    /// </summary>
    internal static class FakeInput
    {
        private static GameKey key;
        private static int frame = -1;

        public static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.GetKeyDown)), postfix: new HarmonyMethod(typeof(FakeInput), nameof(Down)));
            h.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.GetKey)), postfix: new HarmonyMethod(typeof(FakeInput), nameof(Down)));
            h.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.ClearAllKeysDown)), postfix: new HarmonyMethod(typeof(FakeInput), nameof(ClearAll)));
            h.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.ClearKeyDown)), postfix: new HarmonyMethod(typeof(FakeInput), nameof(Clear)));
            h.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.ClearKey)), postfix: new HarmonyMethod(typeof(FakeInput), nameof(Clear)));
        }

        private static void Down(GameKey key, ref bool __result)
        {
            if (!__result && key == FakeInput.key && Time.frameCount == frame) __result = true;
        }

        private static void ClearAll() => frame = -1;
        private static void Clear(GameKey key) { if (key == FakeInput.key) frame = -1; }

        /// <summary>Press and release: the key is down during the next frame.</summary>
        public static IEnumerator Press(GameKey k)
        {
            key = k;
            frame = Time.frameCount + 1;
            yield return null;
            yield return null;
        }
    }
}
