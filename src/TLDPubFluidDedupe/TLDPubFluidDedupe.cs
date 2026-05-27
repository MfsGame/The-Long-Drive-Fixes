using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TLDPubFluidDedupe
{
    // Dedupes + rate-limits sns.STank broadcasts — the fluid-state broadcasts that, per direct
    // measurement of the bridge data under NetSim conditions, account for ~40% of all TLD MP
    // bandwidth (~30k packets in 5 minutes, ~107/sec).
    //
    // Stock TLD calls STank every time a tank's fluid contents change in any way — even if
    // it's the same value as last broadcast, even if changes are happening 30+ times per
    // second on a single tank during draining/filling animations. Under real-network
    // conditions (latency + drops + bandwidth caps), this swamps the more important position
    // updates and inflates the per-packet drop count.
    //
    // What this patches:
    //   sns.STank(int itemid, int tankid, List<mainscript.fluid> fluids) — prefix.
    //   We hash the (fluids) list into a compact (type, amount) array, compare element-by-
    //   element against the last broadcast for the same (itemid, tankid) within an epsilon,
    //   AND rate-limit to no more than one broadcast per TankRateLimitMs window. If the new
    //   state is functionally identical to the previous OR we're inside the rate-limit
    //   window with a previous broadcast pending, skip.
    //
    // Ported from MPPatch v2.11's TankDedupeHook with no behavioral changes.

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reedo.tld.pubfluiddedupe";
        public const string PluginName = "TLD Public Fluid Dedupe";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<int> CfgRateLimitMs;
        internal static ConfigEntry<float> CfgAmountEpsilon;
        internal static ConfigEntry<bool> CfgVerbose;

        internal static int skips;
        internal static int passes;

        private void Awake()
        {
            Log = Logger;
            CfgEnabled = Config.Bind("FluidDedupe", "Enabled", true,
                "Master toggle. When on, sns.STank broadcasts that match the previous broadcast " +
                "for the same (itemid, tankid) within AmountEpsilon are dropped, AND any broadcast " +
                "within RateLimitMs of the previous successful broadcast is dropped. Off = stock " +
                "behavior (which sends ~107 fluid packets/sec, ~40% of all TLD MP bandwidth).");
            CfgRateLimitMs = Config.Bind("FluidDedupe", "RateLimitMs", 250,
                "Minimum time between broadcasts for any single (itemid, tankid) pair. 250ms = " +
                "4Hz max per tank, which is more than enough for visible fluid changes. Range " +
                "(0, 5000). 0 disables rate-limiting (dedupe-only).");
            CfgAmountEpsilon = Config.Bind("FluidDedupe", "AmountEpsilon", 0.005f,
                "Absolute fluid-amount difference (per fluid type within the tank) below which " +
                "the new state is considered identical to the previous and the broadcast is " +
                "dropped. 0.005 = 5 milliliters; finer than the player can ever observe.");
            CfgVerbose = Config.Bind("FluidDedupe", "Verbose", false,
                "Log periodic summaries of skip/pass counts. Confirms the dedupe is firing.");

            var harm = new Harmony(PluginGuid);
            try { harm.PatchAll(typeof(STankHook)); }
            catch (Exception ex) { Log.LogError("Failed to patch sns.STank: " + ex.Message); }

            Log.LogInfo("TLD Public Fluid Dedupe v" + PluginVersion + " loaded. Enabled=" +
                CfgEnabled.Value + " RateLimitMs=" + CfgRateLimitMs.Value);
        }

        [HarmonyPatch(typeof(sns), "STank", new Type[] { typeof(int), typeof(int), typeof(List<mainscript.fluid>) })]
        public static class STankHook
        {
            private struct FE { public int type; public float amount; }
            private static readonly Dictionary<long, FE[]> lastSnap = new Dictionary<long, FE[]>();
            private static readonly Dictionary<long, float> lastSendT = new Dictionary<long, float>();
            private static float lastSummary;

            [HarmonyPrefix]
            public static bool Prefix(int itemid, int tankid, List<mainscript.fluid> fluids)
            {
                if (!CfgEnabled.Value) return true;
                if (fluids == null) return true;

                long key = ((long)itemid << 32) | (uint)tankid;
                float t = Time.realtimeSinceStartup;
                float rate = Mathf.Clamp(CfgRateLimitMs.Value, 0, 5000) / 1000f;

                // Rate-limit gate first (cheap)
                if (rate > 0f && lastSendT.TryGetValue(key, out float prevT) && (t - prevT) < rate)
                {
                    skips++;
                    MaybeLogSummary(t);
                    return false;
                }

                // Build current snapshot
                FE[] cur = new FE[fluids.Count];
                for (int i = 0; i < fluids.Count; i++)
                {
                    // mainscript.fluid has fields .type (int) and .amount (float) in stock TLD.
                    cur[i] = new FE { type = (int)fluids[i].type, amount = fluids[i].amount };
                }

                // Compare to last snapshot for this key
                if (lastSnap.TryGetValue(key, out FE[] prev) && prev.Length == cur.Length)
                {
                    float eps = CfgAmountEpsilon.Value;
                    bool same = true;
                    for (int i = 0; i < cur.Length; i++)
                    {
                        if (prev[i].type != cur[i].type || Math.Abs(prev[i].amount - cur[i].amount) > eps)
                        {
                            same = false; break;
                        }
                    }
                    if (same)
                    {
                        skips++;
                        MaybeLogSummary(t);
                        return false;
                    }
                }

                lastSnap[key] = cur;
                lastSendT[key] = t;
                passes++;
                MaybeLogSummary(t);
                return true; // let stock STank run
            }

            private static void MaybeLogSummary(float t)
            {
                if (!CfgVerbose.Value) return;
                if (t - lastSummary < 5f) return;
                lastSummary = t;
                int total = skips + passes;
                if (total == 0) return;
                float pct = 100f * skips / total;
                Log.LogInfo($"[FluidDedupe] last 5s: skipped={skips} passed={passes} ({pct:F0}% saved)");
                skips = 0; passes = 0;
            }
        }
    }
}
