#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
playtest_app="${PLAYTEST_APP:-}"
log_dir="$(mktemp -d "${TMPDIR:-/tmp}/godotstation-multiplayer-smoke.XXXXXX")"
host_log="$log_dir/host.log"
client_log="$log_dir/client.log"
host_pid=""
client_pid=""
smoke_mode="${1:-movement}"

cleanup() {
  if [[ -n "$client_pid" ]]; then kill "$client_pid" 2>/dev/null || true; fi
  if [[ -n "$host_pid" ]]; then kill "$host_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT

# PLAYTEST_APP is an exported macOS .app; PLAYTEST_BIN is any exported game executable.
if [[ -n "${PLAYTEST_BIN:-}" ]]; then
  godot_bin="$PLAYTEST_BIN"
  playtest_app="$PLAYTEST_BIN"
elif [[ -n "$playtest_app" ]]; then
  godot_bin="$playtest_app/Contents/MacOS/Godotstation"
fi
if [[ ! -x "$godot_bin" ]]; then
  echo "Godot executable not found: $godot_bin" >&2
  exit 2
fi

launch_host() {
  if [[ -n "$playtest_app" ]]; then
    if [[ "$smoke_mode" == "--pull" ]]; then
      "$godot_bin" --headless --quit-after 1500 -- --playtest-host --playtest-jobid=20 --playtest-smoke-pull
    elif [[ "$smoke_mode" == "--examine" ]]; then
      "$godot_bin" --headless --quit-after 600 -- --playtest-host --playtest-jobid=20 --playtest-smoke-examine
    elif [[ "$smoke_mode" == "--ghost" ]]; then
      "$godot_bin" --headless --quit-after 20000 -- --playtest-host --playtest-jobid=20 --playtest-smoke-ghost
    elif [[ "$smoke_mode" == "--health" ]]; then
      "$godot_bin" --headless --quit-after 9000 -- --playtest-host --playtest-jobid=20 --playtest-smoke-health
    elif [[ "$smoke_mode" == "--power-loop" ]]; then
      "$godot_bin" --headless --quit-after 4800 -- --playtest-host --playtest-jobid=2 --playtest-smoke-power-loop
    elif [[ "$smoke_mode" == "--round-restart" ]]; then
      "$godot_bin" --headless --quit-after 900 -- --playtest-host --playtest-jobid=20 --playtest-smoke-construction --playtest-smoke-round-restart
    elif [[ "$smoke_mode" == "--construction" ]]; then
      "$godot_bin" --headless --quit-after 900 -- --playtest-host --playtest-jobid=20 --playtest-smoke-construction
    elif [[ "$smoke_mode" == "--engineering-access" ]]; then
      "$godot_bin" --headless --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-engineering-access
    elif [[ "$smoke_mode" == "--interactions" ]]; then
      "$godot_bin" --headless --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-interactions
    else
      "$godot_bin" --headless --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-move --playtest-smoke-chat
    fi
  else
    if [[ "$smoke_mode" == "--pull" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 1500 -- --playtest-host --playtest-jobid=20 --playtest-smoke-pull
    elif [[ "$smoke_mode" == "--examine" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 600 -- --playtest-host --playtest-jobid=20 --playtest-smoke-examine
    elif [[ "$smoke_mode" == "--ghost" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 20000 -- --playtest-host --playtest-jobid=20 --playtest-smoke-ghost
    elif [[ "$smoke_mode" == "--health" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 9000 -- --playtest-host --playtest-jobid=20 --playtest-smoke-health
    elif [[ "$smoke_mode" == "--power-loop" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 4800 -- --playtest-host --playtest-jobid=2 --playtest-smoke-power-loop
    elif [[ "$smoke_mode" == "--round-restart" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 900 -- --playtest-host --playtest-jobid=20 --playtest-smoke-construction --playtest-smoke-round-restart
    elif [[ "$smoke_mode" == "--construction" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 900 -- --playtest-host --playtest-jobid=20 --playtest-smoke-construction
    elif [[ "$smoke_mode" == "--engineering-access" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-engineering-access
    elif [[ "$smoke_mode" == "--interactions" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-interactions
    else
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-move --playtest-smoke-chat
    fi
  fi
}
launch_client() {
  if [[ -n "$playtest_app" ]]; then
    if [[ "$smoke_mode" == "--pull" ]]; then
      "$godot_bin" --headless --quit-after 1500 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-pull
    elif [[ "$smoke_mode" == "--examine" ]]; then
      "$godot_bin" --headless --quit-after 600 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-examine
    elif [[ "$smoke_mode" == "--ghost" ]]; then
      "$godot_bin" --headless --quit-after 20000 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-ghost
    elif [[ "$smoke_mode" == "--health" ]]; then
      "$godot_bin" --headless --quit-after 9000 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-health
    elif [[ "$smoke_mode" == "--power-loop" ]]; then
      "$godot_bin" --headless --quit-after 4800 -- --playtest-join=127.0.0.1 --playtest-jobid=20 --playtest-smoke-power-loop
    elif [[ "$smoke_mode" == "--round-restart" ]]; then
      "$godot_bin" --headless --quit-after 900 -- --playtest-join=127.0.0.1 --playtest-jobid=20 --playtest-smoke-construction --playtest-smoke-round-restart
    elif [[ "$smoke_mode" == "--construction" ]]; then
      "$godot_bin" --headless --quit-after 900 -- --playtest-join=127.0.0.1 --playtest-jobid=20 --playtest-smoke-construction
    elif [[ "$smoke_mode" == "--engineering-access" ]]; then
      "$godot_bin" --headless --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-engineering-access
    elif [[ "$smoke_mode" == "--interactions" ]]; then
      "$godot_bin" --headless --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-interactions
    else
      "$godot_bin" --headless --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-move --playtest-smoke-chat
    fi
  else
    if [[ "$smoke_mode" == "--pull" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 1500 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-pull
    elif [[ "$smoke_mode" == "--examine" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 600 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-examine
    elif [[ "$smoke_mode" == "--ghost" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 20000 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-ghost
    elif [[ "$smoke_mode" == "--health" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 9000 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-health
    elif [[ "$smoke_mode" == "--power-loop" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 4800 -- --playtest-join=127.0.0.1 --playtest-jobid=20 --playtest-smoke-power-loop
    elif [[ "$smoke_mode" == "--round-restart" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 900 -- --playtest-join=127.0.0.1 --playtest-jobid=20 --playtest-smoke-construction --playtest-smoke-round-restart
    elif [[ "$smoke_mode" == "--construction" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 900 -- --playtest-join=127.0.0.1 --playtest-jobid=20 --playtest-smoke-construction
    elif [[ "$smoke_mode" == "--engineering-access" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-engineering-access
    elif [[ "$smoke_mode" == "--interactions" ]]; then
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-interactions
    else
      "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-move --playtest-smoke-chat
    fi
  fi
}

launch_host >"$host_log" 2>&1 &
host_pid=$!

for _ in $(seq 1 80); do
  if grep -q "PLAYTEST_HOST_READY" "$host_log"; then break; fi
  if ! kill -0 "$host_pid" 2>/dev/null; then cat "$host_log"; exit 1; fi
  sleep 0.1
done
if ! grep -q "PLAYTEST_HOST_READY" "$host_log"; then
  cat "$host_log"
  echo "Host did not become ready." >&2
  exit 1
fi

launch_client >"$client_log" 2>&1 &
client_pid=$!

max_checks=100
if [[ "$smoke_mode" == "--construction" ]]; then max_checks=200; fi
if [[ "$smoke_mode" == "--round-restart" ]]; then max_checks=200; fi
if [[ "$smoke_mode" == "--power-loop" ]]; then max_checks=900; fi
if [[ "$smoke_mode" == "--health" ]]; then max_checks=900; fi
if [[ "$smoke_mode" == "--ghost" ]]; then max_checks=1400; fi
if [[ "$smoke_mode" == "--pull" ]]; then max_checks=200; fi
if [[ "$smoke_mode" == "--engineering-access" ]]; then max_checks=100; fi
for _ in $(seq 1 "$max_checks"); do
  ready=0
  if [[ "$smoke_mode" == "--round-restart" ]]; then
    if grep -q "PLAYTEST_GIRDER_PLATED host_state=true metal_sheets_remaining=0" "$host_log" && \
       grep -q "PLAYTEST_ROUND_ENDED round=1" "$host_log" && \
       grep -q "PLAYTEST_ROUND_ENDED_CLIENT round=1" "$client_log" && \
       grep -q "PLAYTEST_ROUND_RESTARTED round=2 phase=PreRound" "$host_log" && \
       grep -q "PLAYTEST_ROUND_RESTARTED_CLIENT round=2 crew=0 metal=2 plated=False" "$client_log" && \
       grep -q "PLAYTEST_ROUND_RESTART_OK round=2 crew=2 metal=2 plated=false airlock=false wrench_owner=0" "$host_log" && \
       grep -q "PLAYTEST_ROUND_RESTART_CLIENT_OK round=2 crew=2 metal=2 plated=false" "$client_log" && \
       grep -q "PLAYTEST_ROUND_REPORT_HOST round=1 received=true tracked_state=true" "$host_log" && \
       grep -q "PLAYTEST_ROUND_REPORT_CLIENT round=1 received=true tracked_state=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--construction" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -q "PLAYTEST_GIRDER_PLATED host_state=true metal_sheets_remaining=0" "$host_log" && \
       grep -q "PLAYTEST_GIRDER_PLATING_OK engineer=true sheets_consumed=2 duration_seconds=4 replicated=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--engineering-access" ]]; then
    if grep -q "PLAYTEST_ENGINEERING_ACCESS_DENIED peer=" "$host_log" && \
       grep -q "PLAYTEST_ENGINEERING_AIRLOCK_OPENED peer=1 clearance=Engine" "$host_log" && \
       grep -q "PLAYTEST_ENGINEERING_ACCESS_OK denied=true engineer_open=true replicated=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--pull" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -Eq "PLAYTEST_PULL_STARTED puller=[0-9]+ target=1\$" "$host_log" && \
       grep -q "PLAYTEST_PULL_HOST_FOLLOWED behind=True" "$host_log" && \
       grep -Eq "PLAYTEST_PULL_STOPPED puller=[0-9]+ target=1 reason=OutOfReach" "$host_log" && \
       grep -q "PLAYTEST_PULL_HOST_OK followed=true broke_free=true" "$host_log" && \
       grep -q "PLAYTEST_PULL_CLIENT_STARTED target=1" "$client_log" && \
       grep -q "PLAYTEST_PULL_CLIENT_OK started=true released_by_distance=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--examine" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -Eq "PLAYTEST_EXAMINE peer=[0-9]+ target=9001 result=Ok" "$host_log" && \
       grep -Eq "PLAYTEST_EXAMINE peer=[0-9]+ target=1 result=Ok" "$host_log" && \
       grep -Eq "PLAYTEST_EXAMINE peer=[0-9]+ target=9010 result=OutOfView" "$host_log" && \
       grep -q "PLAYTEST_EXAMINE_RECEIVED seen=True first=That's a wrench." "$client_log" && \
       grep -q "PLAYTEST_EXAMINE_RECEIVED seen=True first=This is Crew 1, Engineer." "$client_log" && \
       grep -q "PLAYTEST_EXAMINE_RECEIVED seen=False first=You cannot see that from here." "$client_log" && \
       grep -q "PLAYTEST_EXAMINE_CLIENT_OK object=true crew=true walled_off=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--ghost" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -q "PLAYTEST_CREW_STAT peer=1 from=HardCrit to=Dead" "$host_log" && \
       grep -q "PLAYTEST_INCAPACITATED_ACTION_REJECTED peer=1 stat=Dead" "$host_log" && \
       grep -q "PLAYTEST_CREW_GHOSTED peer=1" "$host_log" && \
       grep -q "PLAYTEST_GHOST_HOST_THROUGH_WALL wall=True body_stayed=True" "$host_log" && \
       grep -Eq "PLAYTEST_LOCAL_CHAT_RELAYED sender=[0-9]{2,} recipients=2 channel=LOCAL" "$host_log" && \
       grep -q "PLAYTEST_LOCAL_CHAT_RELAYED sender=1 recipients=1 channel=DEAD" "$host_log" && \
       grep -q "PLAYTEST_GHOST_HOST_OK heard_living=true" "$host_log" && \
       grep -q "PLAYTEST_GHOST_CLIENT_SAW_DEATH victim=1" "$client_log" && \
       grep -q "PLAYTEST_GHOST_CLIENT_OK body_dead=true body_stayed=True ghost_hidden=True" "$client_log" && \
       ! grep -Eq "PLAYTEST_LOCAL_CHAT_RECEIVED speaker=1\$" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--health" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       [[ $(grep -c "PLAYTEST_CREW_ATTACKED attacker=" "$host_log") -ge 20 ]] && \
       grep -q "PLAYTEST_CREW_STAT peer=1 from=Stable to=SoftCrit" "$host_log" && \
       grep -q "PLAYTEST_HEALTH_HOST_CRIT health=0" "$host_log" && \
       grep -q "PLAYTEST_INCAPACITATED_ACTION_REJECTED peer=1 stat=SoftCrit" "$host_log" && \
       grep -q "PLAYTEST_HEALTH_HOST_BLOCKED hands_blocked=True" "$host_log" && \
       grep -Eq "PLAYTEST_CREW_TREATED healer=[0-9]+ patient=1 healed=40 health=40" "$host_log" && \
       grep -q "PLAYTEST_CREW_STAT peer=1 from=SoftCrit to=Stable" "$host_log" && \
       grep -q "PLAYTEST_HEALTH_HOST_RECOVERED crit=True blocked=True can_act=True health=40" "$host_log" && \
       grep -q "PLAYTEST_HEALTH_CLIENT_SAW_CRIT victim=1 health=0" "$client_log" && \
       grep -q "PLAYTEST_ACTION_MESSAGE channel=COMBAT" "$client_log" && \
       grep -q "PLAYTEST_HEALTH_CLIENT_OK crit=true treated=true victim_health=40 packs=5 replicated=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--power-loop" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -q "PLAYTEST_POWER_HOST_ON" "$host_log" && \
       grep -q "PLAYTEST_POWER_HOST_OUTAGE energy=0" "$host_log" && \
       grep -q "PLAYTEST_POWER_HOST_RECOVERED shared=true" "$host_log" && \
       grep -q "PLAYTEST_POWER_CLIENT_ON replicated=true" "$client_log" && \
       grep -q "PLAYTEST_POWER_CLIENT_OUTAGE replicated=true" "$client_log" && \
       grep -Eq "PLAYTEST_POWER_LOOP_CLIENT_OK engineer=[0-9]+ job=20 role=Engineer on=True outage=True recovery=True replicated=true" "$client_log"; then
      ready=1
    fi
  elif [[ "$smoke_mode" == "--interactions" ]]; then
    if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -q "PLAYTEST_ROUND_STARTED host_job=20" "$host_log" && \
       grep -q "PLAYTEST_SHARED_INTERACTION_OK pickup=true wrench_use=true dropped=true replicated=true" "$client_log" && \
       [[ $(grep -c "PLAYTEST_INTERACTION_SERVER_STATE" "$host_log") -ge 3 ]]; then
      ready=1
    fi
  elif grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
       grep -q "PLAYTEST_HOST_READY port=7777 phase=PreRound" "$host_log" && \
       grep -q "PLAYTEST_ROUND_STARTED host_job=20" "$host_log" && \
       grep -q "PLAYTEST_LOCAL_CHAT_RELAYED sender=1 recipients=2" "$host_log" && \
       [[ $(grep -c "PLAYTEST_LOCAL_CHAT_RELAYED sender=" "$host_log") -ge 2 ]] && \
       grep -q "PLAYTEST_LOCAL_CHAT_RECEIVED speaker=1" "$client_log" && \
       [[ $(grep -c "PLAYTEST_LOCAL_CHAT_RECEIVED speaker=" "$client_log") -ge 2 ]] && \
       grep -q "PLAYTEST_PLAYER_SPAWNED peer=1" "$client_log" && \
       grep -q "PLAYTEST_PLAYER_SPAWNED peer=1 job=20 role=Engineer" "$client_log" && \
       grep -Eq "PLAYTEST_PLAYER_SPAWNED peer=[0-9]+ job=2 role=Assistant count=2" "$client_log" && \
       grep -q "PLAYTEST_CLIENT_POSITION peer=" "$host_log" && \
       grep -q "PLAYTEST_REMOTE_MOVE peer=" "$client_log"; then
    ready=1
  fi

  if (( ready )); then
    wait "$client_pid"
    client_pid=""
    wait "$host_pid"
    host_pid=""
    if grep -q "ERROR:\|SCRIPT ERROR" "$host_log" "$client_log"; then
      cat "$host_log" "$client_log"
      echo "Godot reported a runtime error. Logs: $log_dir" >&2
      exit 1
    fi
    if [[ "$smoke_mode" == "--round-restart" ]]; then
      echo "MULTIPLAYER_ROUND_RESTART_SMOKE_OK: two clients saw the end and respawned in a fresh shared round"
    elif [[ "$smoke_mode" == "--construction" ]]; then
      echo "MULTIPLAYER_CONSTRUCTION_SMOKE_OK: engineer picked up two sheets, completed the four-second girder task, and both clients observed the wall"
    elif [[ "$smoke_mode" == "--pull" ]]; then
      echo "MULTIPLAYER_PULL_SMOKE_OK: the remote peer pulled the host, the host followed one tile behind, and walking away broke the pull"
    elif [[ "$smoke_mode" == "--examine" ]]; then
      echo "MULTIPLAYER_EXAMINE_SMOKE_OK: the remote peer examined an object and a crew member, and was refused a look through walls"
    elif [[ "$smoke_mode" == "--ghost" ]]; then
      echo "MULTIPLAYER_GHOST_SMOKE_OK: the killed host left its body, crossed a wall as a ghost hidden from the living, heard the living, and spoke only to the dead"
    elif [[ "$smoke_mode" == "--health" ]]; then
      echo "MULTIPLAYER_HEALTH_SMOKE_OK: harm-intent wrench hits put the host in crit, crit blocked its hands, and a bruise pack revived it on both peers"
    elif [[ "$smoke_mode" == "--power-loop" ]]; then
      echo "MULTIPLAYER_POWER_LOOP_SMOKE_OK: Engineer fuel, generator, storage, replicated APC lamp outage, and recovery were observed by both peers"
    elif [[ "$smoke_mode" == "--engineering-access" ]]; then
      echo "MULTIPLAYER_ENGINEERING_ACCESS_SMOKE_OK: Assistant ID was denied; Engineer ID opened the shared airlock on both clients"
    elif [[ "$smoke_mode" == "--interactions" ]]; then
      echo "MULTIPLAYER_INTERACTION_SMOKE_OK: remote peer picked up, used, and dropped the shared wrench; snapshots replicated"
    else
      echo "MULTIPLAYER_SMOKE_OK: shared shift started, distinct roles spawned, local speech routed, and movement replicated"
    fi
    echo "Logs: $log_dir"
    exit 0
  fi

  if ! kill -0 "$host_pid" 2>/dev/null || ! kill -0 "$client_pid" 2>/dev/null; then
    cat "$host_log" "$client_log"
    echo "A multiplayer smoke process exited early." >&2
    exit 1
  fi
  sleep 0.1
done

cat "$host_log" "$client_log"
echo "Two-peer $smoke_mode smoke timed out. Logs: $log_dir" >&2
exit 1
