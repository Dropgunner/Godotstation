# Fidelity Contract: Crew Health, Incapacitation, and Recovery

**Status:** implemented in the engine and LAN adapter with deterministic rule and shared-world tests. A two-peer `--health` smoke is scripted and added to CI. It has not run in the session that wrote it, because executing a downloaded Godot binary was not permitted there. A hands-on ordinary-controls playtest is still open.

**Source check:** tgstation [`f37d36c`](https://github.com/tgstation/tgstation/tree/f37d36c295c4bf991b72647281352f3bd471b335), read directly on 2026-11-05. Unitystation's HandApply bridge (intent carried by the client click) is unchanged from the [interaction contract](FidelityContract_InteractionSystem.md).

## Upstream behavior and evidence

- [`code/__DEFINES/mobs.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/__DEFINES/mobs.dm) sets `MAX_LIVING_HEALTH 100`. [`code/__DEFINES/combat.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/__DEFINES/combat.dm) sets `HEALTH_THRESHOLD_CRIT 0`, `HEALTH_THRESHOLD_FULLCRIT -30`, `HEALTH_THRESHOLD_DEAD -100`, and `CLICK_CD_MELEE 8` (deciseconds).
- `carbon/update_stat` in [`carbon.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/mob/living/carbon/carbon.dm) dies at ≤ dead threshold, enters `HARD_CRIT` at ≤ hard-crit threshold, `SOFT_CRIT` at ≤ crit threshold, otherwise `STABLE`.
- `living/set_stat` in [`living.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/mob/living/living.dm) adds `FLOORED`, `HANDS_BLOCKED`, `INCAPACITATED`, `FORCE_WHISPER` in soft crit, and `DEAF`, `FLOORED`, `HANDS_BLOCKED`, `INCAPACITATED`, `KNOCKEDOUT` in hard crit and death.
- `carbon/handle_breathing` and `breathe` in [`carbon/life.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/mob/living/carbon/life.dm) breathe every fourth `SSmobs` tick, or every tick after a failed breath. `SSmobs` ticks every 2 s. Hard crit adds 1 to `losebreath` per breath and soft crit adds 0.25. A missed breath goes through `handle_suffocation` at zero pressure: +3 oxygen loss at or above crit, +1 below it. A good breath at or above crit heals 5 oxygen loss.
- `living_say.dm` returns before speaking when unconscious and forces whisper (`message_range = 1`) under `TRAIT_FORCE_WHISPER`; the dead use `say_dead`.
- [`wrench.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/game/objects/items/tools/engineering/wrench.dm) gives `/obj/item/wrench` `force = 5`.
- [`stacks/medical.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/game/objects/items/stacks/medical.dm): medical stacks start at `amount = 6`. `bruise_pack` has `heal_brute = 40`, `self_delay = 4 SECONDS`, `other_delay = 2 SECONDS`, and does not work on the dead.

## Contract implemented in Godotstation

1. Every crew member has server-owned `CrewHealth` (brute and oxygen loss, health, stat). The stat ladder and its thresholds follow `update_stat` exactly. Death is final for the round.
2. Crew bodies are entities in the shared station model (`kind = crew`, id = peer id). They carry health, stat, brute, oxygen loss, job and treatment progress, and they replicate and hit-test through the same snapshot path as every other object.
3. Clicks carry the selected intent: 1 help, 2 disarm, 3 grab, 4 harm. A wrench in the active hand on harm intent deals 5 brute to a reachable crew body, then imposes the attacker's 0.8 s melee cooldown. Help intent and empty hands deal no damage. Twenty hits put a healthy crew member into soft crit.
4. Soft crit, hard crit and death block hands. The server rejects interactions, hand swaps and drops, and cancels girder work and treatments in progress. Soft crit crawls (the engine's crawling speed), hard crit and death cannot move. Soft crit is forced to whisper (1-tile range), hard crit cannot speak, and the dead speak only to the dead. Hard-crit and dead crew do not receive local speech or combat lines.
5. Life ticks follow the source breath cadence. Untreated soft crit drifts into hard crit, and hard crit loses 1 health per 2 s until death at −100.
6. A six-pack bruise pack stack sits in Medical. With a pack in the active hand, clicking an injured, living crew body starts a 2 s treatment, or 4 s on yourself. It completes only while the healer stays capable, keeps the pack in the active hand, and stays in reach. It heals 40 brute and consumes one pack. Healing back above 0 health restores stable status and hand use.
7. Attacks, treatments and stat changes are sent as `[COMBAT]`/`[ACTION]` lines to the participants and to conscious crew within 14 tiles with line of sight. Avatars lie down when floored, grey out in death, and show their condition to every peer. The local HUD shows health, stat and treatment progress.
8. The round-end report counts crew as stable, critical or dead from the replicated crew bodies.

## Known gaps before parity claims

- No limbs, wounds, bleeding, burn/toxin damage, armour, or body-zone targeting. The bruise pack heals total brute instead of one limb, and it does not loop the way tgstation's stacks do.
- Only the wrench is a weapon. Disarm and grab intents are selectable, but nothing handles them yet. Unarmed attacks, help-up, pulling and examine are not implemented.
- The melee cooldown gates melee only; tgstation's `next_move` blocks every click.
- There is no ghost observer mode, revival, defibrillation, or cloning. A dead player stays in the dead body until the round restarts.
- The tgstation `say` range is 7 tiles; local speech keeps the Unitystation 14-tile fallback (see the [speech contract](FidelityContract_LocalSpeech.md)).
- Station air is assumed breathable, so atmospherics does not yet drive suffocation.
- The smoke stages player positions on the host so it does not depend on scripted walking; movement is covered by the movement smoke.

## Regression coverage

`CrewHealthTests` cover the stat thresholds, death finality, healing caps, breath cadence in soft and hard crit, oxygen recovery, the per-stat traits, and bruise pack delays and cancellation. `SharedCrewHealthWorldTests` cover crew-body replication and removal, peer-id collision rejection, harm-intent damage and its events, help-intent and empty-hand rejection, the melee cooldown, reach, crit blocking hands and cancelling construction, treatment of others and of yourself, treatment rejection and cancellation, death by untreated hard crit, and round-report counts.

`tools/multiplayer_smoke.sh --health` runs an Engineer host and an Assistant client. The client picks up the wrench and hits the host on harm intent 20 times until the host collapses. The host then confirms its hands are blocked. The client takes a bruise pack from Medical and treats the host until both peers see the host stable again, at 40 health, with 5 packs left.
