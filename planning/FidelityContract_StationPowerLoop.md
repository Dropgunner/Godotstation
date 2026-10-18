# Fidelity Contract: Engineer Power Loop

**Status:** implemented in the LAN adapter with deterministic rule and snapshot tests. The two-peer power-loop smoke passed on the integrated game code; a hands-on ordinary-controls playtest and exact source-game power-network behavior remain open.

**Source check:** Unitystation `b0bb5b77b33e2e3e61de1a17254664f626834263` and tgstation `f37d36c295c4bf991b72647281352f3bd471b335`, inspected 2026-10-18.

## Upstream behavior and evidence

- Unitystation's pinned [`PowerGenerator.cs`](https://github.com/unitystation/unitystation/blob/b0bb5b77b33e2e3e61de1a17254664f626834263/UnityProject/Assets/Scripts/US13/Objects/Engineering/PowerGenerator.cs) accepts either an empty hand or an item with an allowed fuel trait. A fuel interaction transfers/combines the held stack into the generator's internal slot; while running, it consumes a stored sheet only when its internal fuel time runs out, and turns off when no sheet remains (lines 199–249, 257–279). Its periodic update also applies configured current and fluctuating voltage to the electrical node (lines 257–263). This differs from the prototype's one-unit-per-interaction fuel loading and is a deliberate design simplification, not parity.

- Unitystation's [SMES](https://github.com/unitystation/unitystation/blob/b0bb5b77b33e2e3e61de1a17254664f626834263/UnityProject/Assets/Scripts/US13/Objects/Engineering/SMES.cs) lets an empty hand toggle output; tools adjust input/output. The [APC](https://github.com/unitystation/unitystation/blob/b0bb5b77b33e2e3e61de1a17254664f626834263/UnityProject/Assets/Scripts/US13/Objects/Engineering/APC.cs) derives state from voltage and affects connected devices.
- tgstation's [SMES](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/power/smes.dm) models finite charge with input/output attempts and network load/surplus. These references establish dependencies and failure/recovery semantics, not equivalent numeric rates or a complete electrical simulation.

## Contract implemented in Godotstation

1. In this prototype, the Engineer picks up a three-unit stack and loads one unit per generator interaction. This deliberately differs from Unitystation's internal fuel slot and automatic sheet consumption. Starting requires loaded fuel; operation consumes finite fuel and adds energy to finite storage.
2. An empty-hand interaction toggles storage output. The shared APC lamp is powered only when output is enabled and stored energy is positive. A second player observes lamp state through the host's authoritative snapshot.
3. Fuel exhaustion stops generation. Enabled output drains stored energy until the lamp goes out. Loading the held reserve fuel and restarting generation restores the lamp.
4. All requests go through the server's existing interaction dispatcher and reach validation. Fuel, generator, storage, energy, output, and lamp state live in the engine-independent shared world; clients submit requests and render replicated state.
5. Rates are explicitly playtest-scaled: 4 seconds per unit, 2 energy/second generated, 1 energy/second load, and 8 units of capacity. They are placeholders for a short deterministic failure/recovery loop, not balance or Unitystation/tgstation numerical parity.

## Known gaps before parity claims

- This is a single isolated generator/storage/lamp dependency, not a station electrical grid. Cable topology, voltage, APC circuits, power priorities, load variation, tools, and grid failures remain unimplemented. The generator's internal fuel slot, stack transfer/autoload behavior, fuel-time reservoir, and voltage variation are also not ported.
- The scripted smoke verifies two-peer transport and replicated consequence for this power loop only. It does not replace the ordinary-controls two-person task or a source-runtime comparison.
- Godot 4.4.1 Mono command-line startup and the exported macOS app's headless startup passed on 2026-10-18. This establishes local runtime availability for those checks, not hands-on playability.
- The current smoke exercises fuel exhaustion and recovery. It does not yet cover the full requested outage-report-action loop, a second failure/recovery cycle, or end/restart after the power task.
- REA inventoried a bounded source corpus; no Unitystation or tgstation runtime trace was captured, so exact behavior beyond the inspected source path remains unverified.

## Regression coverage

`StationPowerLoopTests` covers charging, output load, outage, reserve-fuel recovery, invalid fuel, and invalid time. `SharedStationInventoryWorldTests` covers Engineer pickup/refueling/start/output through validated interactions. `ReplicatedStationPowerTests` covers client-visible state and hit-target availability. `tools/multiplayer_smoke.sh --power-loop` passed for two local peers on 2026-10-18; the ordinary-controls human task is still open.

## Verification recorded 2026-10-18

- `dotnet build game/Godotstation.Game.sln`: passed on integrated commit `c1d2b64` with 0 warnings and 0 errors.
- `bash tools/preflight.sh --fast`: passed; this mode explicitly skips build and test execution.
- The test project targets `net10.0`, but the installed SDK is .NET 8. `dotnet test tests/Godotstation.Tests/Godotstation.Tests.csproj` therefore failed with `NETSDK1045`. With a temporary, automatically restored retarget to `net8.0`, the suite passed: 255 passed, 8 skipped, 263 total. The tracked project file was restored unchanged.
- `GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot tools/multiplayer_smoke.sh --power-loop`: passed on `7808126`; both peers observed the generator/storage/APC outage and recovery. The later `c1d2b64` merge adds only the game solution file.
- `bash tools/export_playtest_macos.sh /private/tmp/Godotstation-Integrated-c1d2b64-Playtest.app`: produced a universal app with PCK and arm64/x86_64 C# runtime assemblies; the exported executable started and exited headlessly without runtime errors. Godot emitted a `DirectoryNotFoundException` while cleaning an already-absent temporary .NET publish directory after writing the bundle. The export script completed its bundle and startup checks; the cleanup diagnostic remains visible.

These checks establish rules, local transport for the scripted scenario, and app startup. They do not establish completion of the manual two-player task or `/tg/station`/Unitystation parity.
