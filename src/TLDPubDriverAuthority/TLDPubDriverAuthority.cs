using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TLDPubDriverAuthority
{
    // Fixes the "rev but the car doesn't move" symptom in MP.
    //
    // Stock TLD's carscript.UpdMulti runs every frame on every car. When the car is being driven
    // multiplayer-style, it merges received carfloats (steering, throttle, brake, clutch, horn)
    // from the network into the local driver's input state. The merge is bidirectional: it
    // sums local input + received "multi" input. Under perfect-network conditions this is fine;
    // under real Steam P2P (or NetSim-emulated conditions: 100ms±50ms latency, 4% drop) the
    // received values are *what the driver intended 100-150ms ago*. Mixed back into the
    // driver's current inputs, that delay-shifted echo zeroes out the driver's actual W press
    // → throttle effectively pegged to "what I was doing 100ms ago, which was nothing because
    // I just started pressing W."
    //
    // Net symptom: driver holds W, engine revs (RPM does respond to local throttle), but the
    // car doesn't actually accelerate because the merge resets throttle each frame. Visible
    // identically on host AND client because the bug is in the merge path that runs on
    // whichever side is driving.
    //
    // Fix (ported from MPPatch v2.x):
    //   Prefix-replace carscript.UpdMulti. When this car is being driven locally
    //   (driving || driving2), don't merge — broadcast OUR inputs as carfloats and zero the
    //   incoming-multi fields so they don't get consumed. When the car is being driven
    //   remotely or sitting empty, let stock run normally.
    //
    // Risks:
    //   None observed in earlier v2.x testing. The DriverAuthority hook was the *one* claim/
    //   authority change in v2.x that didn't cause issues — the suspension/wheel chaos came
    //   from ClaimDrivenCars and ClaimMovingItems, not this hook.

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.reedo.tld.pubdriverauthority";
        public const string PluginName = "TLD Public Driver Authority";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static int skips;

        private void Awake()
        {
            Log = Logger;
            CfgEnabled = Config.Bind("DriverAuthority", "Enabled", true,
                "Master toggle. When on, the carscript.UpdMulti input-merge is replaced with " +
                "a direct broadcast of our local inputs when we're driving — stops echoed " +
                "stale inputs from overwriting our current W/A/S/D under network latency. " +
                "Off = stock behavior, which causes 'rev but no movement' under any meaningful " +
                "ping or packet loss.");
            CfgVerbose = Config.Bind("DriverAuthority", "Verbose", false,
                "Log per-5s summary of how many UpdMulti calls had the merge skipped. Useful " +
                "for confirming the patch is actually firing on the car you're driving.");

            var harm = new Harmony(PluginGuid);
            try { harm.PatchAll(typeof(CarUpdMultiHook)); }
            catch (Exception ex) { Log.LogError("Failed to patch carscript.UpdMulti: " + ex.Message); }

            Log.LogInfo("TLD Public Driver Authority v" + PluginVersion + " loaded. Enabled=" + CfgEnabled.Value);
        }

        [HarmonyPatch(typeof(carscript), "UpdMulti")]
        public static class CarUpdMultiHook
        {
            [HarmonyPrefix]
            public static bool Prefix(carscript __instance, bool tolva)
            {
                if (!CfgEnabled.Value) return true;
                if (mainscript.M == null || !mainscript.M.multi) return true;
                if (__instance == null || !__instance.setid) return true;
                if (!__instance.driving && !__instance.driving2) return true;

                // We're the driver of this car. Broadcast OUR inputs directly — no merge with
                // network-delivered values that may be stale by 100ms+.
                try
                {
                    float bikeGas = 0f;
                    try { bikeGas = Traverse.Create(__instance).Field("bikeGas").GetValue<float>(); }
                    catch { }

                    if (sns.s != null)
                    {
                        sns.s.SCarFloats(__instance.carid,
                                         __instance.isteer,
                                         __instance.currenthorn,
                                         __instance.ithrottle,
                                         __instance.ibrake,
                                         __instance.iclutch,
                                         bikeGas);
                    }

                    // Zero the "multi" input shadows so the stock branch (if it runs later for
                    // any reason) doesn't add stale echoed values onto our locals.
                    __instance.msteer = 0f;
                    __instance.mhorn = 0f;
                    __instance.mgas = 0f;
                    __instance.mbrake = 0f;
                    __instance.mclutch = 0f;
                    __instance.mbikeGas = 0f;
                    __instance.multiControlling = false;

                    skips++;
                    return false; // skip stock UpdMulti merge entirely
                }
                catch (Exception ex)
                {
                    Log.LogWarning("[DriverAuthority] threw: " + ex.Message + " — falling back to stock");
                    return true;
                }
            }
        }

        private void Update()
        {
            if (!CfgVerbose.Value) return;
            if (Time.realtimeSinceStartup - _lastSum < 5f) return;
            _lastSum = Time.realtimeSinceStartup;
            if (skips > 0)
            {
                Log.LogInfo("[DriverAuthority] last 5s: " + skips + " merge-skips (= you were driving for that many frames)");
                skips = 0;
            }
        }
        private float _lastSum = -1f;
    }
}
