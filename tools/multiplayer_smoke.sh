#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
playtest_app="${PLAYTEST_APP:-}"
log_dir="$(mktemp -d /private/tmp/godotstation-multiplayer-smoke.XXXXXX)"
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

if [[ -n "$playtest_app" ]]; then
  godot_bin="$playtest_app/Contents/MacOS/Godotstation"
fi
if [[ ! -x "$godot_bin" ]]; then
  echo "Godot executable not found: $godot_bin" >&2
  exit 2
fi

launch_host() {
  if [[ -n "$playtest_app" ]]; then
    if [[ "$smoke_mode" == "--round-restart" ]]; then
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
    if [[ "$smoke_mode" == "--round-restart" ]]; then
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
    if [[ "$smoke_mode" == "--round-restart" ]]; then
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
    if [[ "$smoke_mode" == "--round-restart" ]]; then
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
       grep -q "PLAYTEST_ROUND_RESTART_CLIENT_OK round=2 crew=2 metal=2 plated=false" "$client_log"; then
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
