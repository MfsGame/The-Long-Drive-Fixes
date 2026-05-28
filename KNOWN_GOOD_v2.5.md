# TLD Multiplayer Fixes — v2.5 Known-Good Snapshot

**Date archived:** 2026-05-27
**What's new vs v2.4:** UX bug fix — Enter key now works in the join-password dialog.

## v2.5 changes

| Plugin | Version | Change |
|---|---|---|
| **TLDPubPasswordEnter** | **0.1.0** | **NEW.** Stock TLD's `snl.IF_Password` input field has no `onSubmit` / `onEndEdit` wiring — pressing Enter in the password field does nothing, you have to mouse-click the OK button. This plugin watches Enter/KeypadEnter while the password panel is showing, the field is focused, and the text is non-empty, then invokes `snl.PressedPasswordOk()` (the same call the OK button makes). |

All v2.4 plugins carry over unchanged.

## Full plugin lineup (13 gameplay + 2 dev-only)

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
| TLDPubRemoteCarKinematic | 0.5.0 | Kinematic remote cars (otherClaimed-aware) |
| TLDPubClaimDrivenCar | 0.1.0 | Claim car on get-in (fixes client engine-stall) |
| **TLDPubPasswordEnter** | **0.1.0** | **Enter key works in join-password dialog (NEW)** |

**Dev-only (shipped in /public but inert by default):**

| Plugin | Version | Role |
|---|---|---|
| TLDPubLoopback | 0.15.0 | Local two-instance file-bridge + NetSim + lobby fake |
| TLDPubFakeId | 1.0.0 | Per-instance SteamID swap (required for loopback) |
