#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

mode="write"
base="HEAD"
while (($#)); do
	case "$1" in
	--check)
		mode="check"
		shift
		;;
	--base)
		if (($# < 2)); then
			printf '%s\n' '--base requires a Git revision.' >&2
			exit 64
		fi
		base="$2"
		shift 2
		;;
	*)
		printf 'Usage: %s [--check] [--base GIT_REVISION]\n' "$0" >&2
		exit 64
		;;
	esac
done

# global.json pins 10.0.400 with rollForward latestPatch, so any 10.0.4xx SDK is valid.
actual_sdk="$(dotnet --version 2>/dev/null || true)"
if [[ ! "$actual_sdk" =~ ^10\.0\.4[0-9][0-9]$ ]]; then
	printf 'Zombieland requires a .NET SDK 10.0.4xx (global.json); dotnet resolved to %s.\n' "${actual_sdk:-unavailable}" >&2
	exit 2
fi

temporary_dir="$(mktemp -d "${TMPDIR:-/tmp}/zombieland-format.XXXXXX")"
trap 'rm -rf "$temporary_dir"' EXIT

format_arguments=(whitespace --no-restore -v quiet)
if [[ "$mode" == "check" ]]; then
	format_arguments+=(--verify-no-changes)
fi

main_status=0
bridge_status=0
dotnet format Source/ZombieLand.csproj "${format_arguments[@]}" >"$temporary_dir/main.log" 2>&1 || main_status=$?
dotnet format Source/BridgeTools/Zombieland.BridgeTools.csproj "${format_arguments[@]}" >"$temporary_dir/bridge.log" 2>&1 || bridge_status=$?
if ((main_status != 0 || bridge_status != 0)); then
	printf 'C# formatting %s failed.\n' "$mode" >&2
	if [[ -s "$temporary_dir/main.log" ]]; then
		printf '%s\n' 'Main project formatter output:' >&2
		cat "$temporary_dir/main.log" >&2
	fi
	if [[ -s "$temporary_dir/bridge.log" ]]; then
		printf '%s\n' 'BridgeTools formatter output:' >&2
		cat "$temporary_dir/bridge.log" >&2
	fi
	exit 2
fi

xml_arguments=(--changed --base "$base" --validate-runtime)
if [[ "$mode" == "check" ]]; then
	xml_arguments=(--check "${xml_arguments[@]}")
fi
python3 scripts/format-xml.py "${xml_arguments[@]}"
