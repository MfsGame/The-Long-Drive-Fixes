# TLD Multiplayer Fixes — v2.4 Known-Good Snapshot

**Date archived:** 2026-05-27
**Validated by:** Reedo on real Steam MP with a friend after v2.3 reported "client can't keep engine running."

## What's new in v2.4

Fixes the "client can't drive" symptom from v2.3 by adding claim-on-get-in (the missing piece from MPPatch v2.9 that v2.1 deliberately excluded for safety, now re-enabled because v2.3's RemoteCarKinematic eliminates the prior failure mode):

| Plugin | Version | Change |
|---|---|---|
| **TLDPubClaimDrivenCar** | **0.1.0** | **NEW.** `fpscontroller.GetIn` postfix calls `tosaveitemscript.Claim(true)` on the car; `GetOut` releases. Whichever player is driving becomes physics-authoritative. Eliminates the 100ms-each-way input round-trip that caused stock-TLD's `shutdownRpm` check to stall the engine when a client tried to drive. |
| **TLDPubRemoteCarKinematic** | **0.5.0** | Updated. Authority check now respects `tosaveitemscript.otherClaimed`. When a client claims a car the host is also rendering, host goes kinematic on that car too — stops double-simulating and lets the client's authoritative broadcasts render cleanly. v0.1.0 only handled the *client's* view of a host-driven car; v0.5.0 handles the *host's* view of a client-driven car symmetrically. |

All other v2.3 plugins are unchanged.

## Why this fixes the engine-stall

Stock TLD without claim transfer is host-authoritative for every car. When a client drives:
1. Client presses W
2. SCarFloats broadcast travels to host (~100ms one-way)
3. Host's UpdMulti merges into mgas, physics applies throttle
4. Host broadcasts position back (~100ms)
5. Client sees position update

Net 200ms+ round-trip for every input frame. Between input packets, host's `mgas` zeros out (UpdMulti clears the multi shadows every frame). RPM dips below `shutdownRpm`, engine cuts. Symptom: engine "just dies" any time the client tries to actually drive.

With claim-on-get-in:
1. Client gets in → Claim(true) → client is authoritative
2. Client's `driving2 = true`, local physics runs
3. W press is processed by client's local sim immediately — no round-trip
4. Client broadcasts position; host renders kinematically (RemoteCarKinematic v0.5)
5. Engine state determined by client's direct input → no false stalls

## Full plugin lineup (12 gameplay + 2 dev-only)

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
| TLDPubCarSync | 0.1.0 | 20Hz host car broadcast |
| **TLDPubRemoteCarKinematic** | **0.5.0** | Kinematic remote cars (now respects otherClaimed) |
| **TLDPubClaimDrivenCar** | **0.1.0** | Claim car on get-in, release on get-out (NEW) |

**Dev-only (shipped in /public but inert by default):**

| Plugin | Version | Role |
|---|---|---|
| TLDPubLoopback | 0.15.0 | Local two-instance file-bridge + NetSim + lobby fake |
| TLDPubFakeId | 1.0.0 | Per-instance SteamID swap (required for loopback) |

## Reverting to this exact set later

```
git checkout known-good-v2.4
```
