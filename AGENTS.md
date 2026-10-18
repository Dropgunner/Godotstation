See CODEX_MASTER_PROMPT.md for historical project context and
planning/UNITYSTATION_PARITY_ROADMAP.md for the current parity and playtest gate.

For each delivery cycle, integrate dependent PRs in order before expanding the
feature surface. Build and test from the merged commit; an open PR is proposed
work, not delivered progress. Follow the player path through ordinary controls:
two people join, move, communicate, pick up equipment, complete a sourced
Engineer task, observe its shared result, and end or restart the round. Keep
the gate open until that path is demonstrated in an exported app.

Use .NET tests for rules, two-client runtime checks for transport, and a
hands-on build for playability. Fix replicated state and click targeting when
the path exposes them; grow one shared entity model as objects are added,
without a speculative rewrite. Use Jev only for an unresolved bounded
decision, and stronger coding review at new subsystem boundaries. Track the
integrated build, new player actions, gate result, and bugs found during play
in the roadmap; PR, session, and test counts are supporting evidence only.
