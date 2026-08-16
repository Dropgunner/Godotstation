# Fidelity Contract: Unitystation Interaction System

**Scope**: The interaction pipeline (`DefaultWillInteract`, interaction types, intents,
reach). Everything not enumerated here may be rewritten idiomatically without ceremony.
Everything enumerated here gets a regression test before its implementing code is touched,
and the test survives forever.

A note on selection: the temptation is to contract the *validation code*. That's wrong —
players don't feel `Validations.CanApply`, they feel its refusals. Each item below is
phrased as the refusal or permission a veteran SS13 player has 10,000 hours of muscle
memory around.

---

### FC-1: One tile means one tile

**Assertion**: Given a standard hand interaction (`HandApply` / `PositionalHandApply` /
`TileApply`) against a target whose tile-distance from the performer is ≤ 1 (orthogonal or
diagonal), validation must succeed; at any distance > 1 it must fail — with no
interpolation, no "close enough" epsilon on tile boundaries, and identical results whether
the target is an object or a tile.

**Test**: Parameterized test placing performer at (0,0) and target at every offset in
[-3..3]²; assert `WillInteract == (max(|dx|,|dy|) <= 1)`. Repeat with target mid-animation
between tiles to pin which position (registered tile, not visual position) is authoritative.

**Rationale**: Reach is the fundamental spatial contract of SS13 combat and theft. Players
know to the pixel where they can be grabbed from. An epsilon that lets 1.4-tile diagonal
interactions through changes every melee fight on the station.

---

### FC-2: Telekinesis works only with an empty hand

**Assertion**: Given a performer whose brain has telekinesis, a `HandApply` on a distant
target must succeed **only if the active hand is empty**; the same click with any item in
the active hand must fall back to standard 1-tile reach and fail. A performer without the
telekinesis brain trait must never receive extended reach, and interactions created with
`AllowTelekinesis == false` must use standard reach even for a telekinetic with an empty
hand.

**Test**: 2×2×2 matrix (has-TK × empty-hand × allow-TK flag) against a target 5 tiles away;
exactly the (true, true, true) cell succeeds.

**Rationale**: This is the entire balance of the telekinesis genetic power: you can *touch*
at range but not *use tools* at range. Letting a TK player wrench bolts from across the
room is a game-breaking buff players would notice within one round.

---

### FC-3: Drag-and-drop needs a clear line, not adjacency

**Assertion**: Given a `MouseDrop` of object A onto object B, validation must require
(a) the performer can interact with A under standard rules, AND (b) an unobstructed
line exists between A's world position and B's world position (`IsReachableByPositions`)
— evaluated with **server-side** positions when validating on the server. A and B being
individually reachable by the performer is not sufficient if a wall separates them.

**Test**: Performer adjacent to both A and B, with A and B on opposite sides of a wall
corner: must fail. Same layout without the wall: must succeed. Assert the server variant
reads `AssumedWorldPosServer()`, not client-reported positions.

**Rationale**: Dragging bodies/canisters around corners and through windows is a classic
exploit family. Veterans know exactly which drag paths are legal; changing this changes
cargo tugging, corpse hiding, and canister rigging.

---

### FC-4: What's in your hand always works

**Assertion**: Given a conscious, unrestrained performer, `HandActivate` (use item in
hand) and `InventoryApply` (slot-to-slot) must validate with **no reach or target-position
check whatsoever** — they succeed regardless of what is adjacent, and they fail only on
actor-state grounds (unconscious, cuffed, ghosted).

**Test**: Performer sealed alone inside a 1-tile welded locker: activating the held
flashlight and moving items between own slots must succeed. Same performer handcuffed:
both must fail.

**Rationale**: "I can always click my own gear" is an invariant players lean on in the
worst moments (activating an emergency oxygen tank while trapped). A stray reach check
here would make items dead inside containers.

---

### FC-5: Ghosts don't click

**Assertion**: Given a player in the ghost state, creating a hand interaction must yield
the invalid interaction (`HandApply.Invalid`) before any validation or network message —
the interaction pipeline is never entered.

**Test**: Set `IsGhosting = true`, call the interaction factory
(`HandApply.ByLocalPlayer`), assert the invalid sentinel is returned and no interaction
message was queued to the server.

**Rationale**: Ghosts observing without affecting is a hard rule of the genre; a single
leaked ghost interaction (even a failed-but-sent one triggering a doorbell or log entry)
is a metagaming channel.

---

### FC-6: Intent decides what the same click does

**Assertion**: Given identical performer, hand content, and target, resolving the click
under each of the four intents (Help / Disarm / Grab / Harm) must select four different
interaction outcomes where the target supports them — empty hand on a person: heal-check /
disarm-attempt / grab / punch respectively — and the *selected* intent must be the one
active at the moment of the click.

**Test**: Table-driven test over the 4 intents against a standard humanoid target with
empty hand; assert the dispatched interaction category per intent.

**Rationale**: Intent-switching mid-combat (harm→disarm feints, help-intent surgery in a
brawl) is the highest-skill expression in SS13 melee. If any intent resolves to the wrong
verb, combat feel is gone at a level every veteran detects in seconds.

---

### FC-7: Click context is frozen at click time

**Assertion**: Given a click, the interaction must carry the intent, active hand slot and
its item, alt-click flag, and targeted body zone **as they were when the click happened**;
changing any of them between click and server processing must not alter the interaction's
content (and alt-click must resolve to its distinct alternate behavior, e.g. alt-click
floor tile ≠ click floor tile).

**Test**: Construct interaction, then mutate UI intent / swap hands / release alt before
processing; assert the interaction's captured fields are unchanged. Assert alt-click on a
supporting target dispatches the alternate interaction path.

**Rationale**: Under 200ms of latency, "what I was doing when I clicked" versus "what my
UI shows now" diverge constantly. The original captures at creation
(`UIManager.CurrentIntent`, `KeyboardInputManager.IsAltActionKeyPressed()` in the factory);
sampling at process time instead would make targeting feel drunk and body-zone surgery
unplayable.

---

### FC-8: The client predicts exactly what the server enforces

**Assertion**: Given any interaction and any world state, `WillInteract` evaluated with
`NetworkSide.Client` and with `NetworkSide.Server` on identical state must return the same
verdict — the rule set is one function parameterized by side, differing only in which
position source it reads, never in logic.

**Test**: Property-based test: for randomized (performer state × target distance × intent ×
hand content) tuples, assert client verdict == server verdict on mirrored state. Run as a
permanent invariant suite, not a one-off.

**Rationale**: Every client/server disagreement is either a rubber-band (client showed an
action the server refused) or a ghost-block (client refused what the server would allow).
Players experience this as "laggy controls" even at 20ms ping. This is the single most
feel-critical property of the whole system.

---

### FC-9: The hand's item gets first refusal

**Assertion**: Given a click where both the held item and the target object define a
matching interaction, the held item's interaction must be offered the interaction first,
and the first component to accept consumes it — exactly one interaction fires per click,
in a fixed, documented component order.

**Test**: Target and held item both register handlers recording invocation order; assert
item handler runs first, target handler is not invoked when the item handler accepts, and
repeated clicks are order-stable.

**Rationale**: "Wrench on table deconstructs, empty hand on table slaps it" is how every
tool in the game selects its verb. If resolution order flips, screwdrivers attack the
airlock instead of opening its panel — the most instantly-noticed class of port regression.

---

## Explicitly *not* in the contract

Chosen exclusions, so nobody white-gloves them by accident: the `typeof(T)` dispatch chain
(an implementation artifact — replace with polymorphism freely), null-propagation
quirks (`OrNull()`), the `PlayerTypes` mask plumbing, message/string formatting, and the
right-click context-menu presentation. If a rewrite changes these and all nine assertions
above still pass, the port is faithful.

---

## Amendments — v1.1, Wave 1 (verified against `DefaultWillInteract.cs`, `HandApply.cs`, `Validations.cs` @ develop)

The clauses above stand unchanged. Reading the actual source pinned constants and exposed
places where the clauses, as first drafted, were looser or stricter than reality:

**A1 — FC-1 exact predicate.** Standard reach is *strict* Chebyshev:
`max(|dx|, |dy|) < 1.5f` (`PlayerScript.INTERACTION_DISTANCE`), measured between
register-tile world positions, plus an unblocked click line
(`MatrixManager.IsPassableAtAllMatrices`, `CollisionType.Click`, Walls/Windows/Grills
excluded *at the destination only* — you may click a window, not through one). A 1.4-tile
diagonal passes; exactly 1.5 does not. Tests must pin strictness and Chebyshev-not-Euclidean.

**A2 — FC-2 exact predicate.** Telekinesis reach is *Euclidean* `distance ≤ 15f`
(`> 15` refuses, so exactly 15 passes) between `AssumedWorldPosServer()` of performer and
target — on *both* network sides — and performs **no blocking linecast**: TK reaches
through walls by design (FOV gating is a different system). `ExtendedServer` reach uses
`INTERACTION_DISTANCE_EXTENDED = 1.75f` for its in-flight catch.

**A3 — FC-3 clarification.** MouseDrop's object-to-object line uses
`AssumedWorldPosServer()` on **both** sides, not just the server — client prediction
already reasons in assumed server space for this check.

**A4 — FC-8 sanctioned asymmetries.** "Client verdict == server verdict on identical
state" holds for the standard paths, but the original is *deliberately* client-permissive
in exactly three places, which the invariant suite must exclude (and pin as permissive):
 1. a **hidden** performer: client always validates true and defers; server permits only
    the container itself (with the faithful quirk that containerless-hidden + null target
    compares `null == null` and passes);
 2. **`ReachRange.ExtendedServer`**: range is never checked client-side;
 3. **conscious state of non-local players**: the client only knows its own, assumes
    everyone else is conscious.

**A5 — In-inventory targets short-circuit the range chain.** A target inside an inventory
is reachable iff that inventory's root player is the performer — and if it is *not*, the
branch chain ends: an item in someone else's inventory is unreachable at **any** reach
range, including Unlimited.

**A6 — dispatch-order hazard.** `PositionalHandApply` *derives from* `HandApply`, while
the original dispatches on exact type (`typeof(T)`). Any runtime-type port must match the
positional case first, or positional clicks silently lose their click-position semantics.
Pinned by test.

**A7 — FC-6 / FC-9 deferral.** Intent-verb selection and hand-item-first resolution live
in the dispatch chain (`InteractionUtils`, interactable components), whose sources are not
yet in scope. Their tests are skip-marked placeholders in
`tests/Godotstation.Tests/Interactions/` until that wave lands; the clauses remain binding.
