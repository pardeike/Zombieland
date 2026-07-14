#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

"$ROOT/scripts/format-project.sh"

stop_rimworld=true
restart_rimworld=false
dotnet_args=()
rimworld_mod_dir_arg=""
for arg in "$@"; do
	case "$arg" in
		--stop-rimworld)
			stop_rimworld=true
			;;
		--no-stop-rimworld)
			stop_rimworld=false
			;;
		--restart-rimworld)
			restart_rimworld=true
			;;
		/p:RIMWORLD_MOD_DIR=*|-p:RIMWORLD_MOD_DIR=*)
			rimworld_mod_dir_arg="${arg#*=}"
			dotnet_args+=("$arg")
			;;
		*)
			dotnet_args+=("$arg")
			;;
	esac
done

rimworld_processes() {
	ps ax -o pid=,comm= | awk '$0 ~ /\/RimWorld by Ludeon Studios$/ { print }' || true
}

stop_running_rimworld() {
	local running="$1"
	printf 'Stopping RimWorld before deploy build:\n%s\n' "$running" >&2
	local pids
	pids="$(printf '%s\n' "$running" | awk '{ print $1 }')"
	if [[ -z "$pids" ]]; then
		return 0
	fi

	kill -TERM $pids 2>/dev/null || true
	for _ in {1..30}; do
		if [[ -z "$(rimworld_processes)" ]]; then
			return 0
		fi
		sleep 1
	done

	printf 'RimWorld did not stop after SIGTERM; refusing to deploy over a running game.\n' >&2
	return 1
}

effective_rimworld_mod_dir="${RIMWORLD_MOD_DIR:-}"
if [[ -n "$rimworld_mod_dir_arg" ]]; then
	if [[ -n "$effective_rimworld_mod_dir" && "$effective_rimworld_mod_dir" != "$rimworld_mod_dir_arg" ]]; then
		printf 'Refusing deploy build because RIMWORLD_MOD_DIR differs between environment and MSBuild property:\n' >&2
		printf '  environment: %s\n' "$effective_rimworld_mod_dir" >&2
		printf '  property:    %s\n' "$rimworld_mod_dir_arg" >&2
		exit 2
	fi
	effective_rimworld_mod_dir="$rimworld_mod_dir_arg"
fi

# Deploy builds intentionally use one root: RIMWORLD_MOD_DIR/../BridgeTools is
# the paired global BridgeTools location for that RimWorld Mods folder.
if [[ -n "$effective_rimworld_mod_dir" ]]; then
	running_rimworld="$(rimworld_processes)"
	if [[ -n "$running_rimworld" ]]; then
		if [[ "$stop_rimworld" == true ]]; then
			stop_running_rimworld "$running_rimworld"
		else
			printf 'Refusing deploy build because RimWorld is still running:\n%s\n' "$running_rimworld" >&2
			printf 'Rerun without --no-stop-rimworld to stop it automatically.\n' >&2
			exit 2
		fi
	fi
fi

if ((${#dotnet_args[@]})); then
	dotnet build Source/ZombieLand.csproj -v:q -clp:ErrorsOnly "${dotnet_args[@]}"
else
	dotnet build Source/ZombieLand.csproj -v:q -clp:ErrorsOnly
fi

if [[ "$restart_rimworld" == true ]]; then
	open 'steam://run/294100'
fi
