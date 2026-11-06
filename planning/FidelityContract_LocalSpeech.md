# Fidelity Contract: Local Speech Routing

## Source

- Unitystation commit: `c911581e8354958d664b1e32087053ce43638790`
- `UnityProject/Assets/Scripts/US13/Core/Chat/ChatRelay.cs`

`ChatRelay.PropagateChatToClients` routes Local, Action, and Combat messages by each
listener's hearing range and rejects listeners behind walls. The default local range is 14
world units when the listener has no `LocalChatRange` stat. The originator always receives
their own message. Unitystation has additional chat channels and special listeners; this
slice implements only player local speech and does not claim radio, whisper, ghost, or system
channel parity.

## Godot contract

1. The server accepts one-line speech only from an assigned crew peer in a started shift.
2. Speech is limited to 256 characters and is routed by authoritative server positions.
3. A speaker always receives their own speech. Other crew hear it only inside their hearing
   range and with a clear grid line through the station map.
4. Closed wall tiles block speech; an open corridor/door tile does not.
5. Speech stays on the local channel; it is never broadcast to every peer by default.
6. The UI renders player text without interpreting it as BBCode.

### Incapacitated speakers and listeners (added 2026-11-05)

Source, same commit, under `UnityProject/Assets/Scripts/US13/`:
`Player/PlayerScript.cs` `GetAvailableChannelsMask` (lines 565–580) sets a dead body's transmit
channels to `Ghost | OOC` and its receive channels to a list that omits `Local`;
`Core/Chat/Chat.cs` `CorrectDeadGhostChannel` (lines 346–356) moves a dead speaker's line onto
`ChatChannel.Ghost`.

7. **FC-LS-7** A dead crew member's spoken line is routed on the Ghost channel, never Local. It
   reaches the speaker and other dead crew only, regardless of range or walls. *Tests:*
   `IncapacitatedSpeechTests.DeadSpeakerIsMovedToGhostChannelAndReachesOnlyDeadCrew`,
   `IncapacitatedSpeechTests.DeadSpeakerIsNotHeardByAdjacentLivingCrewEvenWithClearLine`.
8. **FC-LS-8** A dead crew member does not receive living crew's Local speech. *Test:*
   `IncapacitatedSpeechTests.DeadListenersDoNotReceiveLivingLocalSpeech`.
9. **FC-LS-9** Unconscious and barely-conscious speakers keep ordinary Local routing, because the
   pinned `CorrectDeadGhostChannel` returns without restriction for a crit body that is not dead
   (despite its "Crit players can't talk" comment). *Tests:*
   `IncapacitatedSpeechTests.UnconsciousSpeakerKeepsLocalRoutingAsUpstreamDoes`,
   `IncapacitatedSpeechTests.LivingSpeechStillRespectsRangeAndWalls`.

Open question (not decided here): whether upstream silences crit speech elsewhere (for example
through `Mind.IsMute`, whose setter was not inspected). `Chat.CannotSpeakReason` has text for
unconscious speakers, which hints that it may. Until that is traced, FC-LS-9 keeps the code-path
behaviour. A dead peer who has moved becomes a ghost and then hears all Local speech (FC-GH-3 in
`FidelityContract_SurvivalConsciousness.md`); a dead peer who has not moved follows the dead-body masks.

## Verification

- Engine tests cover default range boundaries, speaker echo, wall occlusion, and an open
  doorway line.
- `tools/multiplayer_smoke.sh` verifies two connected crew members each hear each other's
  locally routed speech in addition to role assignment and movement replication.
- `tools/multiplayer_smoke.sh --survival` has the dead client speak after death and checks that
  the host relays it with `channel=GHOST` to one recipient and the living host never receives it.
