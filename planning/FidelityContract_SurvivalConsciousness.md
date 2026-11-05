# Fidelity Contract: Survival and Consciousness

**Status:** contract written before code (2026-11-04). Scope: one brute-damage pool per crew member,
Unitystation conscious-state thresholds, harm-intent damage through the existing interaction
dispatcher, server-side movement/interaction refusal while unconscious or dead, replication of crew
status to every peer, casualty count in the round-end report, and reset on round restart. It does
not claim parity for body parts, organs, bleeding, oxygen, stamina, revival, ghosts, or medical.

**Source check:** Unitystation `c911581e8354958d664b1e32087053ce43638790`, read 2026-11-04 from the
pinned commit (blobless fetch, no clone). All paths below are under
`UnityProject/Assets/Scripts/US13/`. tgstation `f37d36c295c4bf991b72647281352f3bd471b335` was not
needed for this slice and is not cited.

## Upstream behavior and evidence

- **Health pool and thresholds.** `HealthV2/Living/HealthStateController.cs` lines 32–33 default
  `MaxHealth` to 100 and lines 163–172 make `SetConsciousState` server-only.
  `HealthV2/Living/LivingHealthMasterBase.cs` lines 1148–1209 (`CalculateOverallHealth`) compute
  `currentHealth = MaxHealth − Σ body-part damage` and set `BARELY_CONSCIOUS` when health `< 0`,
  `UNCONSCIOUS` when `< -50`, and route `< -100` to `CheckHeartStatus` → `Death()`; lines 1264–1280
  (`SetConsciousStateRespectHealth`) spell the same thresholds with `DEAD` at `< -100`. Lines
  1790–1800 (`Death`) ignore an already-dead body. Lines 96–120 define `IsDead`, `IsCrit`
  (`UNCONSCIOUS`) and `IsSoftCrit` (`BARELY_CONSCIOUS`).
- **Unarmed harm.** `Items/Weapons/WeaponNetworkActions.cs` lines 93–116 (`ServerPerformMeleeAttack`)
  validate with `Validations.CanApply(..., NetworkSide.Server, apt: CheckState(x => x.CanMelee))`
  before any damage; lines 144–156 use the active arm's stats when no weapon is held; line 214
  applies the damage to the victim's health. `Items/Implants/Limbs/HumanoidArm.cs` lines 11–12
  default `ArmMeleeDamage = 5f` and `ArmDamageType = Brute`. `Core/Input System/MouseInputController.cs`
  lines 220 and 260 branch on `UIManager.CurrentIntent == Intent.Harm`; `Core/Input System/KeybindManager.cs`
  lines 425–428 bind intents Help/Disarm/Grab/Harm to keys 1–4.
- **Consequences of falling.** `HealthV2/Living/PlayerHealthV2.cs` lines 62–110: on the server,
  `BARELY_CONSCIOUS` drops both hand items and switches to crawling; `UNCONSCIOUS` and `DEAD` drop
  hand items and record `ServerAllowInput = false`; `CONSCIOUS` restores input. Only `CONSCIOUS`
  (or buckled) bodies stand.
- **Movement refusal.** `Player/MovementV2/MovementSynchronisation.cs` line 363 syncs
  `allowInput`, and lines 1372–1380 (`CanInPutMove`) gate player moves on it; lines 1146–1149 let
  a dead player who tries to move spawn a ghost instead of moving.
- **Dead actors.** `Player/PlayerScript.cs` lines 509–520 (`IsDeadOrGhost`) and 571–573 (a dead
  player may transmit only Ghost/OOC chat). `LivingHealthMasterBase.cs` lines 2479–2481 give other
  players only "critical condition" / "dead" examine text, not numbers.

## Contract implemented in Godotstation

Each clause names the test that fails when it is violated. Tests live in
`tests/Godotstation.Tests/Health/` and `tests/Godotstation.Tests/Interactions/`.

1. **FC-SC-1 Health pool.** Each crew member has one bounded brute-damage pool with `MaxHealth = 100`.
   `health = 100 − brute`. Conscious state is derived only from health: `Conscious` at `health ≥ 0`,
   `BarelyConscious` at `health < 0`, `Unconscious` at `health < −50`, `Dead` at `health < −100`.
   Damage never goes below 0 and is clamped at 250. *Tests:* `CrewHealthRulesTests.ThresholdsMatchUnitystation`,
   `CrewHealthRulesTests.DamageIsClampedAndNonNegative`.
2. **FC-SC-2 Death is terminal for the round.** Once `Dead`, further damage keeps the body `Dead`
   and nothing in this slice revives it; healing is out of scope. *Test:* `CrewHealthRulesTests.DeathIsTerminal`.
3. **FC-SC-3 Harm application path.** A harm-intent `HandApply` by a crew member on another crew
   member's body resolves through `InteractionDispatcher.TryResolveClick` on the server, subject to
   the existing `DefaultWillInteract` reach/obstruction/conscious-state validation, and applies
   `UnarmedBruteDamage` to the target. Help/Disarm/Grab intents on a crew body produce no outcome in
   this slice. Out-of-reach or blocked harm is refused and applies nothing. *Tests:*
   `SharedStationSurvivalTests.HarmIntentAppliesBruteThroughDispatcher`,
   `SharedStationSurvivalTests.HelpIntentOnCrewAppliesNothing`,
   `SharedStationSurvivalTests.OutOfReachHarmIsRefused`.
4. **FC-SC-4 Actor refusals bind to live state.** The performer's `ConsciousState` seen by the
   dispatcher is the live health-derived state, so FC-4 (`Validations.CanInteract`) refuses every
   interaction, including harm, from an `Unconscious` or `Dead` crew member on the server. *Tests:*
   `SharedStationSurvivalTests.UnconsciousActorCannotInteract`,
   `SharedStationSurvivalTests.DeadActorCannotHarm`, `DispatchOrderTests` (IPerformer live state).
5. **FC-SC-5 Movement refusal.** The world answers `CanActorMove(peerId) == false` for `Unconscious`
   and `Dead` crew, and the server adapter must consult it before resolving a movement input.
   `BarelyConscious` crew may still move. *Test:* `SharedStationSurvivalTests.UnconsciousAndDeadCrewCannotMove`.
6. **FC-SC-6 Falling drops hand items.** When a crew member's state worsens from `Conscious`, both
   hand slots are dropped into the world at the body's position. *Test:*
   `SharedStationSurvivalTests.FallingDropsHeldItems`.
7. **FC-SC-7 One authoritative replicated status.** The server publishes each crew body as a
   station entity (`kind = crew`, `id = −peerId`, fields `brute` and `conscious`) through the
   existing `EntitiesChanged` / `ReceiveEntityStates` path, so every peer sees every peer's status.
   Clients never compute damage. *Tests:* `SharedStationSurvivalTests.CrewStatusReplicatesAsEntityState`,
   `ReplicatedStationEntitiesTests` (crew bodies are click targets).
8. **FC-SC-8 Round report counts casualties.** `RoundEndReport` carries `CrewCasualties`, the number
   of `Dead` crew at round end, and prints it in the display text. *Test:*
   `RoundEndReportTests.ReportCountsCasualties`.
9. **FC-SC-9 Reset on restart.** A round restart creates fresh crew bodies: after
   `SharedRoundLifecycle.TryRestart` the adapter rebuilds the shared world and every re-added crew
   member starts `Conscious` with `brute = 0`. *Test:* `SharedStationSurvivalTests.RestartRestoresFullHealth`.

## Addendum (2026-11-05): bruise-pack treatment, the first recovery path

Source, same commit, under `UnityProject/Assets/Scripts/US13/`: `Items/Medical/HealsTheLiving.cs`
(`WillInteract` requires `DefaultWillInteract.Default`, a target with `LivingHealthMasterBase`, and
`Intent.Help`; `ServerApplyHeal` calls `HealDamage(null, 40, healType, ...)` and
`stackable.ServerConsume(1)`; self-treatment instead runs a 5-second `SelfHeal` progress action).
`UnityProject/Assets/Prefabs/Items/Medical/Basic Treatments/Bruise Pack.prefab` sets
`initialAmount`/`maxAmount` 6 and `healType: 0`; `HealthV2/HealthEnums.cs` line 58 maps 0 to
`Brute`. `LivingHealthMasterBase.CalculateOverallHealth` (lines 1148–1209) re-derives the conscious
state from health after damage changes, so healing above a threshold restores the better state.

10. **FC-HL-1 Item.** The playtest station spawns one bruise-pack stack (`kind = bruise-pack`, id 9010)
    with 6 uses; each application heals 40 brute. *Tests:* `BruisePackHealingTests.StationSpawnsOneSixUseBruisePack`,
    `BruisePackHealingTests.UsesRunOutAfterSixApplications`.
11. **FC-HL-2 Heal rule.** Healing removes brute (never below 0) from a living body and re-derives
    its conscious state with the FC-SC-1 thresholds. A dead body is not healed. *Tests:*
    `BruisePackHealingTests.HealRulesRemoveBruteAndRederiveState`, `BruisePackHealingTests.DeadBodiesAreNotHealed`.
12. **FC-HL-3 Application path.** A Help-intent `HandApply` with the pack in the active hand on
    another living, injured crew body resolves through the dispatcher's reach/obstruction/performer
    validation, heals once, and consumes one use. Wrong intent, no damage, out of reach, or a dead
    body is refused and consumes nothing. *Tests:* `BruisePackHealingTests.HelpIntentWithPackHealsFortyAndConsumesOneUse`,
    `BruisePackHealingTests.PackIsRefusedOnUninjuredDeadSelfOrWrongIntent`, `BruisePackHealingTests.OutOfReachTreatmentIsRefused`.
13. **FC-HL-4 Recovery.** An unconscious crew member treated back to `Conscious` can move and act
    again, and every peer sees the new state through the FC-SC-7 replication. *Tests:*
    `BruisePackHealingTests.HealingRevivesAnUnconsciousCrewmateToMovement`; smoke
    `tools/multiplayer_smoke.sh --recovery`.
14. **FC-HL-5 No self-treatment yet.** Applying the pack to one's own body is refused (deviation below).

Deviations for this addendum: self-treatment (upstream: 5-second progress action) is not ported;
healing targets the whole body instead of the aimed body part; the pack does not stop bleeding or
heal trauma (neither exists here). Open question: upstream `CalculateOverallHealth` would re-derive
state for a healed corpse above −100 unless the heart/brain path keeps it dead; that organ model is
not ported, so this slice keeps FC-SC-2 and refuses to heal dead bodies.

## Deviations (deliberate, playtest-scaled)

- **Damage per unarmed hit is 25, not Unitystation's 5.** Thresholds are kept at source values, so
  the path is 5 hits → BarelyConscious (health −25), 7 → Unconscious (−75), 9 → Dead (−125). This is a
  playtest scaling so two people can reach death in under a minute; it is not parity.
- **Status is replicated numerically to all peers.** Upstream shows other players examine text only.
  The playtest needs both peers to observe the same transition in logs and HUD, so every peer
  receives `brute` and `conscious` for every body. Recorded here as a simplification.
- **BarelyConscious crew move at normal speed.** Upstream switches to crawling; crawl speed and
  prone sprites are not ported.
- **Dead bodies stay in the world as the peer's avatar with no ghost mob.** Upstream spawns a ghost
  when a dead player moves. Here the dead peer simply cannot act; observing continues through the
  existing camera. The dispatcher's `IsGhosting` short-circuit stays unused by this slice.
- **Self-harm is refused.** Upstream validation does not forbid it explicitly; this slice refuses a
  harm click on one's own body to keep misclicks from being self-damaging in a two-person test.

## Known gaps before parity claims

- No body parts, organs, bleeding, oxygen, clone, radiation, stamina, armor, block, trauma, or
  weapon damage: only one brute pool and unarmed hits. Disarm and Grab intents on crew do nothing.
- Only one medical item (bruise pack, FC-HL); no revival or `OnRevive` path, so death still ends
  the peer's agency for the round.
- Dead-speaker routing is now covered by FC-LS-7/8 in
  [FidelityContract_LocalSpeech.md](FidelityContract_LocalSpeech.md); crit (unconscious) speech
  is still allowed pending the open question recorded there.
- `RoundEndReport` casualty count is a mechanical extension of the existing fact-only report; no
  Unitystation or tgstation round-outcome rule was traced for it.
- `// TODO(fidelity)` questions recorded in code: whether `BarelyConscious` crew may melee
  (`Validations.CheckState(x => x.CanMelee)` reads a PlayerState asset not inspected here); this
  slice keeps FC-4's default of refusing soft-crit interactions.

## Verification log (2026-11-04, Linux cloud session, .NET SDK 10.0.114, Godot 4.4.1.stable.mono)

- Baseline on `main` `9e464ef`: `bash tools/preflight.sh` → `"build":"pass","test":"pass"`, 268 passed,
  7 skipped; `tools/multiplayer_smoke.sh` → `MULTIPLAYER_SMOKE_OK`.
- `60b4187` (engine rules + tests): `bash tools/preflight.sh` → `{"ok":true,...,"build":"pass","test":"pass",
  "test_summary":"Passed! - Failed: 0, Passed: 301, Skipped: 7, Total: 308"}`.
- `534f4e2` (adapter + smoke): `GODOT_BIN=$(command -v godot) bash tools/multiplayer_smoke.sh --survival` →
  `MULTIPLAYER_SURVIVAL_SMOKE_OK`; host log shows `PLAYTEST_CREW_STATE ... BarelyConscious brute=125`,
  `Unconscious brute=175`, `Dead brute=225`, `PLAYTEST_MOVE_REFUSED ... conscious=Dead`,
  `PLAYTEST_INTERACTION_REFUSED_INCAPACITATED ... conscious=Dead`, `PLAYTEST_SURVIVAL_HOST_OK hits=9`;
  client log shows the same three state lines and `PLAYTEST_SURVIVAL_CLIENT_OK unconscious=True dead=True`.
  `--interactions` and `--round-restart` smokes still pass; preflight unchanged at 301 passed.
- 2026-11-05 follow-ups on branch `claude/project-thread-vq7vru`: preflight 314 passed / 7 skipped;
  `--survival` (now also checks Ghost-channel speech), `--recovery`, `--interactions`,
  `--construction`, `--round-restart`, `--engineering-access`, `--power-loop`, and the default smoke all print
  their `*_SMOKE_OK` line on Linux with Godot 4.4.1 mono.
- Not done: exported macOS app and the hands-on two-person run. See
  [PLAYTEST_KIT_Survival.md](PLAYTEST_KIT_Survival.md).
