# TLD Multiplayer Fixes — v2.1 Known-Good Snapshot

**Date archived:** 2026-05-26
**Validated by:** Reedo on Linux+Proton against a real Steam friend with ~100ms fluctuating ping, AND under NetSim-emulated conditions (100ms ± 50ms latency, 4% packet loss) using the local two-instance Loopback test setup.

## What this is

The first plugin combination that demonstrably improves The Long Drive's stock multiplayer behavior under real network conditions, without breaking anything in the process. After several iterations of more aggressive experiments (claim-based authority transfer, position smoothing with extrapolation, fake Steam matchmaking) that introduced their own bugs, this is the conservative subset that survives in-game testing.

## Plugins in this bundle

| Plugin | Version | What it fixes | Risk |
|---|---|---|---|
| TLDMPUnlock | 2.0.0 | Flips `mainScript2.disableMultiplayer=false` so the Multiplayer button is visible in the main menu. Required for any MP at all on the public branch. | none — stock fix |
| TLDPubMPPatch | 1.0.0 | Stock baseline: ForceReliableSends, ForceMultiFlag. No claim, no smoothing, no dedupe — just the original two settings. | none |
| TLDPubMPDiag | 1.0.0 | Per-msgType packet counter (read-only). Useful for diagnosing what's flowing. | none |
| TLDPubDevMode | 1.2.0 | Forces `mainscript.M.rizseshusi=true` so CapsLock-fly + F4/F8/F3/End dev keys work, reveals the misc-menu toggle. Pure dev convenience, doesn't touch MP code. | none |
| TLDDirectMP | 0.2.0 | TCP-fallback transport (off by default). | none if off |
| **TLDPubBodyPush** | 0.1.0 | Postfix on `pushablescript.PushLocal`. When in MP and the push isn't an echo from an inbound packet, broadcasts via `sns.s.SPush`. Stock TLD only broadcasts hand-raycast pushes — body-collision / vehicle-bump / dropped-item pushes silently die locally and never reach the other end. This wins them propagation. | low |
| **TLDPubPlayerStable** | 0.1.0 | Prefix-replaces `snTempPlayerSync.Update` with the same loop shape but a configurable timeout (default 5s instead of stock 1.5s). Stops the "friend pops out of seat and back in" symptom that fires on any 1.5s+ stutter on the remote end. | low |
| **TLDPubFluidDedupe** | 0.1.0 | Prefix on `sns.STank` — skips broadcasts that match the previous value for the same (itemid, tankid) within an epsilon AND rate-limits to ≥250ms between broadcasts. Direct measurement showed stock TLD emits ~32,000 fluid packets per 5 minutes (40% of all MP bandwidth); this kills ~95% of them, freeing bandwidth for position updates. | low |
| **TLDPubDriverAuthority** | 0.1.0 | Prefix-replaces `carscript.UpdMulti` when the local player is driving. Stops echoed multi-input (`msteer/mgas/mbrake/...`) from clobbering the driver's local inputs under latency — was the actual cause of the "rev but no movement" symptom under marginal connections. Was the *one* claim/authority change from MPPatch v2.x that didn't cause issues. | low (validated in v2.x testing) |

**Total: 9 plugins, all additive Harmony patches.** No claim system, no position smoothing, no fake Steam matchmaking, no transport replacement.

## What was explicitly excluded (and why)

These were tried, broke things, removed:

- **ClaimDrivenCars** / **ClaimMovingItems**: claiming cars to transfer position authority to the driver. Caused suspension oscillation because wheel rigidbodies are separate physics objects from the car body, and position-lerp-from-network fought wheel-spring-physics-local. Also ClaimMovingItems caught every spinning wheel because they're separate tosaveitemscripts with high angular velocity. Won't revisit without a different architecture (e.g., make remote cars kinematic).
- **TLDPubItemSmooth** (v0.1, 0.2, 0.3): position-lerp / extrapolation for items. Three iterations all had problems — either snap-back-then-recalculate (no extrapolation) or low-gravity-floating (with extrapolation that double-stacked on top of client's local Unity physics). Won't work as pure position-smoothing; needs to also disable client's local sim or use Rigidbody.MovePosition.
- **TLDPubLoopback fake-matchmaking (v0.11)**: patched 5 SteamMatchmaking methods to make stock code see a synthetic 2-member lobby. Broke real-Steam-MP entirely because the GetLobbyData hook returned empty strings for the seed key when called outside loopback mode, causing the friend's join to fail. Removed; Loopback v0.12 strictly does transport.
- **TLDPubCarSync** (10Hz car broadcast): worked but bandwidth-heavy. Skipped from this snapshot in favor of FluidDedupe which addresses the same bandwidth concern by removing 40% of *unrelated* traffic instead of adding 10× more car traffic. May revisit if cars-specifically still feel choppy after the FluidDedupe headroom freed bandwidth.

## What's still imperfect

- **Item count desync**: under NetSim, host showed 45 items while client showed 171 in the same location. Stream-loading-timing bug — items receive itemPosUpd before host's local streaming has loaded them. Not addressable without changing how itemPlaceRemoveScript streams items.
- **Phantom items below terrain**: occasional items at y=-3000 on one side. Same stream-timing bug — `UpdOrSpawnItem` instantiates an item before terrain at that location is ready, so it falls.
- **Car suspension drift over time**: 1Hz position broadcasts (plus the ~4Hz MorePosUpd burst from stock) still leave the receiving end's local sim running between corrections. Visible as slight side-drift on long cruises. Could be addressed by TLDPubCarSync (10Hz) if bandwidth headroom permits.

## How to install

### Option A: Drop the DLLs manually
1. Have BepInEx 5.4.x installed in your TLD install.
2. Download from this repo's `/public` folder:
   - All 9 listed plugin DLLs into `BepInEx/plugins/`
   - `TLDPubMPUpdater.dll` into `BepInEx/patchers/`
3. Launch. The auto-updater will check this manifest at startup and report "no pending updates" for the stable set.

### Option B: Use TLDPubMPUpdater for ongoing fetches
If `TLDPubMPUpdater.dll` is already in `BepInEx/patchers/`, it'll fetch any newer versions per the manifest automatically. Just make sure the manifest URL it points to is this repo's `main/public-mp.txt`.

## Reverting to this exact set later

```
git checkout known-good-v2.1
```

(tag created against this commit)

Or just re-pull this manifest — anyone whose updater fetches from `Reedo22/The-Long-Drive-Fixes/main/public-mp.txt` lands on this set automatically.

## Dev tooling not shipped here

Lives in `/src` but not in `/public` or the manifest:

- `TLDPubBridge` — HTTP introspect/control surface on `:38080`. Lets you query `/state` `/lobby` `/players` `/items` over curl while the game runs.
- `TLDPubLoopback` v0.12 — file-bridge transport for local two-instance testing, with NetSim (latency/jitter/drop simulation).
- `TLDPubFakeId` — patches `SteamUser.GetSteamID` so two local instances appear as different users.
- `TLDPubItemSmooth` — three iterations of position-smoothing experiments, kept as reference for the inherent dual-sim tug-of-war.
- `TLDPubCarSync` — 10Hz car broadcast experiment.
- `TLDPubMPCapture`, `TLDPubFullLog`, `TLDPubSpawner` — older diagnostics, sources only.
- `ReedoModToolkit` — TLDLoader mod with F11 UI for browsing/enabling/disabling/updating both BepInEx and TLDLoader-format mods.
- `tld_install_tldloader/` — one-shot installer (Mono.Cecil based) that patches Assembly-CSharp.dll + drops TLDLoader runtime files on Linux+Proton. Replicates what TLDworkshop.exe does on Windows.


---

# UPDATE 2026-05-26 (post-archive) — CRITICAL TLDLoader finding

After spending hours diagnosing what looked like progressive MP regressions during a
session, we identified the actual cause by reverting Assembly-CSharp.dll from the
`.pre-tldloader.bak` and removing the TLDLoader runtime files.

## **Installing TLDLoader breaks stock TLD multiplayer.**

Specifically: applying the Cecil patch that adds `Call TLDLoader.ModLoader.InitMainMenu`
to `mainmenuscript.Start` + `Call TLDLoader.ModLoader.dbInit` to `itemdatabase.Awake`,
AND/OR having TLDLoader runtime loaded into Managed, AND/OR running M-ultiTool's
Harmony patches — some combination of these introduces severe regressions to MP that do
not exist on pure stock TLD. Confirmed by:

1. Yesterday's stock-v1.1 + real Steam P2P session was "literally perfect."
2. Same plugin set today after installing TLDLoader = "a mess" — car driving stalls,
   item drift, world desync.
3. Reverting Assembly-CSharp.dll from backup + removing TLDLoader files = "PERFECT
   AGAIN" (user's own words).

We did not pinpoint exactly which TLDLoader/M-ultiTool component is the culprit. Could
be M-ultiTool patching physics or vehicle code, could be TLDLoader's ModCore changing
something at scene load. Empirically: the whole stack breaks MP.

## Recommended posture

- **For solo / creative play**: install TLDLoader + M-ultiTool. They work great for
  that use case. The whole TLDworkshop ecosystem is built around it.
- **For multiplayer**: leave TLDLoader uninstalled (or revert it before joining MP).
  Use only the BepInEx-based plugins in this manifest.

## How to revert TLDLoader on Linux+Proton

```
# in the game install dir
MANAGED="<install>/TheLongDrive_Data/Managed"
cp "$MANAGED/Assembly-CSharp.dll.pre-tldloader.bak" "$MANAGED/Assembly-CSharp.dll"
rm -f "$MANAGED/TLDLoader.dll" "$MANAGED/Mono.Cecil.dll" "$MANAGED/0Harmony.dll"

# move the Mods folder aside (TLDLoader can't load it anyway with Assembly-CSharp
# unpatched, but cleaner)
PFX_DOCS="$HOME/.local/share/Steam/steamapps/compatdata/<AppID>/pfx/drive_c/users/steamuser/Documents/TheLongDrive"
mv "$PFX_DOCS/Mods" "$PFX_DOCS/Mods.disabled-for-mp"

# When you want it back for solo play:
# 1) run the tld_install_tldloader tool again
# 2) rename Mods.disabled-for-mp -> Mods
```

This is what `tld_install_tldloader` does in reverse. We should add a `--uninstall`
flag for symmetry in a future commit.
