# Fidelity Contract: Examine

**Status:** implemented in the engine and LAN adapter with deterministic tests and a two-peer `--examine` smoke in CI. A hands-on ordinary-controls playtest is still open.

**Source check:** tgstation [`f37d36c`](https://github.com/tgstation/tgstation/tree/f37d36c295c4bf991b72647281352f3bd471b335), read directly on 2026-11-10.

## Upstream behavior and evidence

- [`code/_onclick/click.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/_onclick/click.dm): a shift-click calls `ShiftClickOn`, and `atom/ShiftClick` calls `user.examinate(src)`.
- `examinate` in [`code/modules/mob/mob.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/mob/mob.dm) takes a target from the examiner's view. It has no reach or free-hand requirement. Blind mobs examine by touch only.
- `atom/examine` in [`code/game/atom/atom_examine.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/game/atom/atom_examine.dm) returns the name line ("That's …"; mobs use "This is …") followed by the atom's `desc`.
- `stack/examine` in [`stacks/stack.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/game/objects/items/stacks/stack.dm) adds "There are N …s in the stack." or "There is 1 … in the stack."
- `carbon/examine` in [`carbon/examine.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/mob/living/carbon/examine.dm) reports brute damage as minor (under 25), moderate (under 50) or severe bruising. It reports "barely conscious" in soft crit and "isn't responding to anything around them and seems to be asleep" when unconscious. For the dead it reports "limp and unresponsive; there are no signs of life...", with "and their soul has departed..." when no player can return to the body.
- The wrench and bruise pack `desc` strings come from their item definitions.

## Contract implemented in Godotstation

1. Shift-clicking an entity sends an examine request. The server answers only the examiner, and the text appears in their chat log as `[EXAMINE]`.
2. Examining needs sight, not reach or hands. A living examiner needs an unobstructed line to the target. Soft-crit crew can still examine. Hard-crit crew cannot. The dead see through walls.
3. Objects give "That's a/an/some …", a description, and a state line: stack counts in the source wording, cart anchoring, girder progress, airlock open or closed, generator fuel, storage charge and output, and whether the lamp is lit.
4. Crew give "This is Crew N, Job.", what each hand holds, the source bruising tiers, and the source condition line for soft crit, unconsciousness and death. A dead body reads "their soul has departed" once its player has left it as a ghost, because ghosts cannot return here.
5. Items in someone's hands are not examined directly; they appear in the holder's description. Fixtures that cannot be clicked for use, such as the lamp and a plated girder, can still be examined.

## Known gaps before parity claims

- There is no view radius. tgstation limits examine to the client's view (7 tiles by default); here any target with a clear line can be examined.
- Only the wrench and bruise pack descriptions are source text. The other descriptions are written for this prototype's objects.
- Worn clothing and ID are not listed. There is no "examine closer" second click, no examine of floor or wall tiles, and no darkness check.
- Crew use "they" for everyone; tgstation uses the character's pronouns.
- Dead examiners see everything; tgstation ghosts also need the target in view on screen.

## Regression coverage

`ExamineTests` cover object name, description and state lines, crew role, held items, the three bruising tiers and each condition line, the two death lines, sight being required while hands are not, soft crit being able to examine and hard crit not, the dead seeing through walls, and held items being reachable only through their holder.

`tools/multiplayer_smoke.sh --examine` runs an Engineer host and an Assistant client. From the hall, the client examines the wrench and the host and receives both descriptions. It is then refused a look at the bruise packs behind Medical's walls.
