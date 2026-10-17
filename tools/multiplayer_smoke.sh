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
    "$godot_bin" --headless --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-move --playtest-smoke-chat
  else
    "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-host --playtest-jobid=20 --playtest-smoke-move --playtest-smoke-chat
  fi
}
launch_client() {
  if [[ -n "$playtest_app" ]]; then
    "$godot_bin" --headless --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-move --playtest-smoke-chat
  else
    "$godot_bin" --headless --path "$repo_root/game" --quit-after 300 -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-move --playtest-smoke-chat
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

for _ in $(seq 1 100); do
  if grep -q "PLAYTEST_ROSTER peers=2" "$host_log" && \
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
    wait "$client_pid"
    client_pid=""
    wait "$host_pid"
    host_pid=""
    if grep -q "ERROR:\|SCRIPT ERROR" "$host_log" "$client_log"; then
      cat "$host_log" "$client_log"
      echo "Godot reported a runtime error. Logs: $log_dir" >&2
      exit 1
    fi
    echo "MULTIPLAYER_SMOKE_OK: shared shift started, distinct roles spawned, local speech routed, and movement replicated"
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
echo "Two-peer movement smoke timed out. Logs: $log_dir" >&2
exit 1
