using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TLDPubPlayerStable
{
    // Fixes the "remote player disappears for a second then pops back" symptom in MP, including
    // the "passenger seen in driver's seat keeps vanishing and reappearing" case.
    //
    // Why it happens (stock TLD):
    //   snTempPlayerSync.Update destroys any remote player whose last playerPosUpd packet
    //   arrived more than `syncTime * 10` = 1.5 seconds ago. A single 1.5-second stutter on
    //   the OTHER end — GC pause, frame drop, Steam P2P session hiccup, alt-tab — causes the
    //   remote player's GameObject to be destroyed locally. The very next packet recreates
    //   them from scratch via AddOrUpdOne, which is what you see as the "pop". For passengers
    //   it's especially jarring because the player is reparented to the seat's transform each
    //   recreation, so they snap back in.
    //
    // What we change:
    //   Prefix-replace snTempPlayerSync.Update with a near-identical loop, but the destroy
    //   threshold is configurable (default 5s instead of 1.5s). The other path (UpdPos) is
    //   left unchanged. Net result: brief lag spikes don't destroy the avatar; only a real
    //   ~5-second silence does. If a player actually disconnects, Steam removes them from the
    //   lobby, so the timeout-based destroy is mostly redundant for legitimate departures.
    //
    // Why not just raise syncTime: that value is also used as the interpolation reference for
    //   smooth movement, so raising it would slow the visual lerp. The destroy threshold is a
    //   separate consideration.

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reedo.tld.pubplayerstable";
        public const string PluginName = "TLD Public Player Stable";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<float> CfgTimeoutSec;
        internal static ConfigEntry<bool> CfgVerbose;

        internal static int destroyedPlayers;

        private void Awake()
        {
            Log = Logger;
            CfgEnabled = Config.Bind("PlayerStable", "Enabled", true,
                "Master toggle. When on, replaces the stock 1.5-second per-player destroy " +
                "threshold in snTempPlayerSync.Update with a configurable longer value " +
                "(default 5s). Stops remote-player avatars from being destroyed and " +
                "recreated on every brief lag spike — particularly the passenger-in-car case " +
                "where the pop is most visible. Off = stock behavior.");
            CfgTimeoutSec = Config.Bind("PlayerStable", "DestroyTimeoutSec", 5f,
                "Seconds without a playerPosUpd packet before a remote player is destroyed " +
                "locally. Stock value is 1.5. Higher = avatar persists through longer stutters " +
                "but means ghost avatars hang around briefly when someone actually disconnects. " +
                "5 covers typical GC / load-screen / Steam packet hiccups; 10 covers severe network " +
                "blips. Range (0.5, 60).");
            CfgVerbose = Config.Bind("PlayerStable", "Verbose", false,
                "Log every destroy (chatty). Useful for confirming whether the timeout is " +
                "actually firing during your sessions.");

            var harm = new Harmony(PluginGuid);
            try { harm.PatchAll(typeof(UpdateHook)); }
            catch (Exception ex) { Log.LogError("Failed to patch snTempPlayerSync.Update: " + ex.Message); }

            Log.LogInfo("TLD Public Player Stable v" + PluginVersion +
                " loaded. Enabled=" + CfgEnabled.Value + " Timeout=" + CfgTimeoutSec.Value + "s");
        }

        [HarmonyPatch(typeof(snTempPlayerSync), "Update")]
        public static class UpdateHook
        {
            [HarmonyPrefix]
            public static bool Prefix(snTempPlayerSync __instance)
            {
                if (!CfgEnabled.Value) return true;
                if (__instance == null || __instance.players == null) return true;

                float timeout = Mathf.Clamp(CfgTimeoutSec.Value, 0.5f, 60f);
                float now = Time.unscaledTime;
                float dt = Time.unscaledDeltaTime;

                // Same logic shape as stock — single pass, break on the first destroy so we
                // don't trip the modified-collection issue (matches stock's `break` behavior).
                for (int i = 0; i < __instance.players.Count; i++)
                {
                    var p = __instance.players[i];
                    if (p == null) continue;
                    if (now - p.lastTime > timeout)
                    {
                        try
                        {
                            if (p.fT != null) UnityEngine.Object.Destroy(p.fT.gameObject);
                            if (p.T != null) UnityEngine.Object.Destroy(p.T.gameObject);
                        }
                        catch { }
                        __instance.players.RemoveAt(i);
                        destroyedPlayers++;
                        if (CfgVerbose.Value)
                            Log.LogInfo("[PlayerStable] destroyed " + p.ID.m_SteamID
                                + " after " + (now - p.lastTime).ToString("0.0")
                                + "s silence (threshold " + timeout + "s)");
                        return false; // matches stock — only destroy at most one per frame
                    }
                    try { p.UpdPos(now, dt); }
                    catch { }
                }
                return false; // we did the work
            }
        }
    }
}
