# TLD Multiplayer Fixes — v2.3 Known-Good Snapshot

**Date archived:** 2026-05-27
**Validated by:** Reedo on Linux+Proton under NetSim-emulated conditions (100ms latency, two-instance loopback rig). v2.2's conservative four plus two new physics patches that fix the up/down car bouncing symptom v2.2 acknowledged as unsolved.

## What's new in v2.3

v2.2 left two car-related issues unsolved in its "What's still imperfect" section:

> Car suspension drift over time: 1Hz position broadcasts (plus the ~4Hz MorePosUpd burst from stock) still leave the receiving end's local sim running between corrections. Visible as slight side-drift on long cruises. Could be addressed by TLDPubCarSync (10Hz) if bandwidth headroom permits.

v2.3 ships both the proposed CarSync solution *and* a more direct attack on the wheel-rigidbody fight that causes the visible "bouncing like overloaded suspension" symptom under any latency:

| Plugin | Version | What it fixes | Risk |
|---|---|---|---|
| **TLDPubCarSync** | 0.1.0 | Host broadcasts car body position + velocity at a configurable rate (default 20Hz) instead of stock 1Hz. Narrows the receiver's divergence window from ~1000ms to ~50ms. Idle-skip threshold means parked vehicles still use stock 1Hz cadence — bandwidth scales with actively-moving cars. | low |
| **TLDPubRemoteCarKinematic** | 0.1.0 | When another player is driving a car, sets `isKinematic=true` on the car body + wheel rigidbodies on the local side. The receiving end stops simulating wheel suspension on the remote car — no more local physics fighting the host's position broadcasts. Reverts to non-kinematic as soon as the local player enters the car. | low |

All other v2.2 plugins are unchanged.

## Full plugin lineup (11 gameplay + 2 dev-only)

**Public manifest (auto-installed via TLDPubMPUpdater):**

| Plugin | Version | Role |
|---|---|---|
| TLDMPUnlock | 2.0.0 | Unlocks the MP button on the main menu |
| TLDPubMPPatch | 1.0.0 | ForceReliableSends + ForceMultiFlag |
| TLDPubMPDiag | 1.0.0 | Per-msgType packet rate diag (passive) |
| TLDPubDevMode | 1.2.0 | F4/F8/F3/End dev keys + CapsLock fly |
| TLDDirectMP | 0.2.0 | TCP-fallback transport (off by default) |
| TLDPubBodyPush | 0.1.0 | Broadcast pushablescript.PushLocal |
| TLDPubPlayerStable | 0.1.0 | snTempPlayerSync timeout 1.5s → 5s |
| TLDPubFluidDedupe | 0.1.0 | sns.STank epsilon + rate-limit |
| TLDPubDriverAuthority | 0.1.0 | Skip echoed multi-input while driving |
| **TLDPubCarSync** | **0.1.0** | **NEW — 20Hz host car broadcast** |
| **TLDPubRemoteCarKinematic** | **0.1.0** | **NEW — kinematic remote cars** |

**Dev-only (shipped in /public but inert by default):**

| Plugin | Version | Role |
|---|---|---|
| TLDPubLoopback | 0.15.0 | Local two-instance file-bridge + NetSim + lobby fake |
| TLDPubFakeId | 1.0.0 | Per-instance SteamID swap (required for loopback) |

These two are FULLY INERT unless the master `[Testing] Enabled = true` flag is set in `BepInEx/config/com.reedo.tld.publoopback.cfg`. Default OFF for safety — turning them on while connected to a real-friend Steam lobby will break things.

## Enabling two-instance loopback testing

Set up two TLD installs (e.g., separate Steam library entries). On **both** instances:

1. Drop the full v2.3 manifest into `BepInEx/plugins/` and the updater into `BepInEx/patchers/`.
2. Edit `BepInEx/config/com.reedo.tld.publoopback.cfg`:
   - `[Testing] Enabled = true` (master switch — enables Loopback transport + NetSim + FakeMatchmaking together)
   - `[Loopback] Mode = Host` on one install, `Mode = Client` on the other
3. Edit `BepInEx/config/com.reedo.tld.pubfakeid.cfg`:
   - `[FakeId] Enabled = true`
   - `FakeLocalSteamID` set to DIFFERENT values on each install (e.g., 1 and 2). Match each side's value to the OTHER side's `[Loopback] FakePeerSteamID`.

Launch both. Host enters the world first via main menu → CreateLobby. Client uses the lobby browser to join. **Always clear `/tmp/tld-loopback/*.bin` and `lobby.json` between test sessions** — stale state across runs causes the "second attempt always works" pattern.

## What's still imperfect

- **Launch-state hygiene**: first launch after a previous loopback session can bounce until `/tmp/tld-loopback/` is cleared. Wrap your testing in a script that wipes the bridge dir between runs.
- **Steering input lag**: at 100ms one-way latency, the receiving client sees the driver's steering input 100ms late. Position is interpolated cleanly via CarSync but steering feels delayed. Stock TLD doesn't predict input — would need a separate dead-reckoning pass.
- **Initial-state dedupe**: the older FluidDedupe v0.1 doesn't whitelist `SendStartStuff` broadcasts (host's initial world-state send to a joining client). Oil/coolant tanks read as empty until they next legitimately change. Fix proposed in v0.2 (in `/src`) — not shipped here to keep v2.3 conservative. Open for v2.4.

## Reverting to this exact set later

```
git checkout known-good-v2.3
```

Or re-pull this manifest — anyone whose updater fetches from `Reedo22/The-Long-Drive-Fixes/main/public-mp.txt` lands on this set automatically.
