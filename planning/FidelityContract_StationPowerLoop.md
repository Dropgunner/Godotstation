# Fidelity Contract: Engineer Power Loop

**Status:** implemented in the LAN adapter with deterministic rule and snapshot tests; the two-peer smoke is scripted but could not run in this local Godot environment. Exact source-game power-network behavior and ordinary-controls playtest remain open.

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
- The scripted smoke is intended to verify two-peer transport and replicated consequence. It has not yet run here and does not replace the ordinary-controls two-person task or a source-runtime comparison.
- Local runtime execution is currently blocked: the installed Godot 4.4.1 command aborts at startup with `CSSM_ModuleLoad(): One or more parameters passed to a function were not valid.`
- The current smoke exercises fuel exhaustion and recovery. It does not yet cover the full requested outage-report-action loop, a second failure/recovery cycle, or end/restart after the power task.
- REA inventoried a bounded source corpus; no Unitystation or tgstation runtime trace was captured, so exact behavior beyond the inspected source path remains unverified.

## Regression coverage

`StationPowerLoopTests` covers charging, output load, outage, reserve-fuel recovery, invalid fuel, and invalid time. `SharedStationInventoryWorldTests` covers Engineer pickup/refueling/start/output through validated interactions. `ReplicatedStationPowerTests` covers client-visible state and hit-target availability. `tools/multiplayer_smoke.sh --power-loop` is the scripted Assistant/Engineer two-peer check; runtime proof is pending.
