# Playtest kit: survival, incapacitation, and death (two instances, macOS)

Goal: two people on one Mac (or two Macs on one LAN) punch one crew member from healthy to
unconscious to dead with ordinary controls, both see every state change, the dead player can no
longer move or act, and a restart gives everyone a fresh body. Budget: about 20 minutes.

What this run can prove: the ordinary-controls path for the failure/incapacitation item of the
playtest gate (`planning/UNITYSTATION_PARITY_ROADMAP.md`, gate item 5). What it cannot prove:
Unitystation parity. Damage per hit is playtest-scaled (25 instead of 5); see
`planning/FidelityContract_SurvivalConsciousness.md`.

## 1. Build the app (once, ~5 minutes)

```sh
cd /path/to/Godotstation
git fetch origin && git switch wave10/survival-consciousness
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot \
  bash tools/export_playtest_macos.sh /private/tmp/Godotstation-Survival.app
```

The script refuses to overwrite an existing app, so pick a new path per build. If Godot prints a
`DirectoryNotFoundException` about a temporary publish directory after writing the bundle, that is
the known cleanup diagnostic; the app is still complete.

Optional pre-check that needs no clicking (about 15 seconds):

```sh
GODOT_BIN=/Applications/Godot_mono.app/Contents/MacOS/Godot bash tools/multiplayer_smoke.sh --survival
```

Expected last lines: `MULTIPLAYER_SURVIVAL_SMOKE_OK: ...` and `Logs: /tmp/godotstation-multiplayer-smoke.XXXX`.

## 2. Launch two instances with visible logs

Launch from two Terminal windows instead of `open -n`, so the `PLAYTEST_*` markers are captured:

```sh
# Terminal A (host)
/private/tmp/Godotstation-Survival.app/Contents/MacOS/Godotstation 2>&1 | tee ~/godotstation-host.log
# Terminal B (client)
/private/tmp/Godotstation-Survival.app/Contents/MacOS/Godotstation 2>&1 | tee ~/godotstation-client.log
```

1. In A choose the **Engineer** role and **Host Station**. It waits in pre-round on UDP 7777.
2. In B choose **Assistant**, enter `127.0.0.1` (or the host Mac's LAN IPv4), and **Join Station**.
3. In A press **Start shift**. Both windows show two crew avatars. The HUD shows a new line
   `Vitals: Conscious · brute 0/100 · intent HELP (keys 1-4)`.

## 3. The harm → unconscious → dead path (ordinary controls)

Controls are Unitystation's defaults: `1` Help, `2` Disarm, `3` Grab, `4` Harm; WASD moves;
left-click acts on the nearest target within about 0.7 tiles.

1. **Move adjacent.** Host (A) walks next to the client's avatar (B). Reach is about 1.5 tiles
   with a clear line.
2. **Select harm.** In A press `4`. The vitals line now ends with `intent HARM`.
3. **Punch five times.** In A left-click B's avatar five times. Each hit adds 25 brute. After the
   fifth hit B's avatar tints yellow with the caption `BARELY CONSCIOUS` in both windows, and B's
   vitals line reads `BarelyConscious · brute 125/100`. If B was holding the wrench, it drops at
   B's feet. B can still walk (crawl speed is not ported).
4. **Two more hits (7 total).** B's avatar turns grey with `UNCONSCIOUS`. B's status line reads
   `You are unconscious. A crewmate can treat you with the bruise pack (help intent).` In B, press WASD and
   click an object: nothing happens, and A's terminal prints `PLAYTEST_MOVE_REFUSED` and
   `PLAYTEST_INTERACTION_REFUSED_INCAPACITATED` for B's peer id.
3a. **Disarm.** B picks up the wrench (`1`, click it). A presses `2` (Disarm) and clicks B's avatar,
   about twice a second. Each try either fails, makes B drop the wrench (about half the time), or
   rarely knocks B down for 6 seconds (`KNOCKED DOWN` caption; B cannot move or click). Both chat
   logs show a `[COMBAT]` line for each try.
4a. **Optional recovery branch (instead of 5).** In A press `1` (Help), walk to the white
   `BRUISE PACK ×6` beside the spawn and left-click it to pick it up, walk back next to B, and
   left-click B twice. Each click heals 40 brute and uses one pack (`×5`, then `×4`). After the
   second click B is back to `Conscious · brute 95/100` in both windows, B's status reads
   `You came round`, and B can walk again. Then continue from 5 if you also want to test death;
   the pack does nothing on a dead body.
5. **Two more hits (9 total).** B's avatar turns dark red with `DEAD`. Further hits change
   nothing. B still sees the station and chat but cannot move, pick up, or use anything.
   If B types in chat now, A does not see it: dead speech goes to the `[GHOST]` channel only.
   If B presses WASD, B leaves the body as a pale `GHOST` that flies through walls; the camera
   follows it, B hears A's speech from anywhere, and B still cannot click anything. A does not see the ghost.
6. **Try to cheat as B.** Press `4` in B and click A's avatar: refused (B is dead).
7. **End the shift** in A. Both windows receive the round report; it now contains
   `Crew casualties: 1`.
8. **Restart the shift** in A, then re-request roles in B. Both avatars are back to normal tint,
   vitals read `Conscious · brute 0/100`, and B can move again.

Correct looks like: every tint/caption change appears in both windows within a second; the
numbers in both vitals lines agree; the dead player cannot make anything happen on either screen.

## 4. Markers that prove it worked

Check the host log (`~/godotstation-host.log`):

```
PLAYTEST_CREW_STATE peer=<B> conscious=BarelyConscious brute=125
PLAYTEST_CREW_STATE peer=<B> conscious=Unconscious brute=175
PLAYTEST_CREW_STATE peer=<B> conscious=Dead brute=225
PLAYTEST_MOVE_REFUSED peer=<B> conscious=Unconscious    (or Dead)
PLAYTEST_INTERACTION_REFUSED_INCAPACITATED peer=<B> conscious=Unconscious    (or Dead)
PLAYTEST_ROUND_ENDED round=1
PLAYTEST_ROUND_RESTARTED round=2 phase=PreRound
```

And the client log (`~/godotstation-client.log`): the same three `PLAYTEST_CREW_STATE` lines for
B's peer id, plus `PLAYTEST_ROUND_ENDED_CLIENT round=1` and `PLAYTEST_ROUND_RESTARTED_CLIENT round=2`.
The refusal markers are printed once per peer per shift, on the host only.

## 5. Recording a bug

Write one line per observation into the roadmap ledger's "Bugs found during play" row or a GitHub
issue: build commit (`git rev-parse --short HEAD`), which step (3.1–3.8), what each window
showed, and the relevant log lines. Attach both `.log` files. Known limitations that are not bugs:
no crawl speed, no ghost mob, speech still works while unconscious or dead, no healing or revival.

## 6. Clean restart

Pressing **Restart shift** on the host rebuilds the whole shared world (fresh bodies, items back
in place). To start completely over, quit both instances and relaunch from §2.
