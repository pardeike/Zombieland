#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 /path/to/Player.log" >&2
  exit 2
fi

LOG="$1"
if [[ ! -f "$LOG" ]]; then
  echo "missing log: $LOG" >&2
  exit 2
fi

awk '
  function flush() {
    if (block == "") return
    key = block
    gsub(/[0-9]+/, "#", key)
    gsub(/0x[0-9a-fA-F]+/, "0x#", key)
    count[key]++
    if (!(key in sample)) sample[key] = block
    block = ""
  }
  /^[A-Z][A-Za-z]+Exception:/ || /^Exception / || /^Exception printing / || /^Error / || /^Critical error / || /^Config error in / || /^Failed to / || /^Bad string pass / || /^Could not / || /^XML error:/ {
    flush()
    block = $0
    next
  }
  block != "" && (/^[[:space:]]+at / || /^[[:space:]]+in / || /^Verse\./ || /^RimWorld\./ || /^ZombieLand\./) {
    block = block "\n" $0
    next
  }
  { flush() }
  END {
    flush()
    for (key in count)
      printf("---- %dx ----\n%s\n", count[key], sample[key])
  }
' "$LOG" | sort -rn -k2
