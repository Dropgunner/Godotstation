# Fidelity Contract — Round Lifecycle

## Scope

Wave 8 adds a small offline round with one job, one objective requiring three wrench
actions, a time limit, explicit outcomes, and a fresh restart. These rules describe the
prototype slice only; they do not claim parity with an unspecified Unitystation round.

## FC-1: Round start and reset

Starting a round MUST begin in `InProgress`, set objective progress to zero, and restore
the configured time limit. Reset MUST return to `NotStarted` with the same initial values.
Starting again after either terminal outcome MUST produce the same fresh state.

**Tests:** verify initial values, reset, and restart after success and failure.

## FC-2: Objective progress

While a round is `InProgress`, a valid wrench action MUST advance progress by exactly one.
Actions with another tool or actions after a terminal outcome MUST NOT advance progress.
Reaching the configured requirement MUST produce `Success`.

**Tests:** verify valid progress, rejected tool, exact completion, and terminal immutability.

## FC-3: Time limit and failure

While a round is `InProgress`, advancing by a finite non-negative elapsed duration MUST
reduce remaining time without going below zero. Reaching zero before objective completion
MUST produce `Failure`. Time MUST not advance after either terminal outcome. Invalid negative,
NaN, or infinite durations MUST be rejected.

**Tests:** verify partial time, timeout failure, no post-terminal changes, and invalid inputs.

## FC-4: Presentation and restart

The frontend MUST display the selected job, objective progress, remaining whole seconds, and
terminal outcome. It MUST offer a restart path that creates a new round in the same running
application. The frontend MUST forward actions and elapsed time to the round engine; it MUST
not decide objective completion or win/loss.

**Smoke:** select the available job, enter the station, wrench three relay panels for success
or wait for timeout for failure, then restart and verify progress and time are fresh.
