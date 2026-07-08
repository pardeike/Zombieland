#!/usr/bin/env bash
set -euo pipefail

usage() {
	cat >&2 <<'USAGE'
Usage:
  rimworld-deploy-guard.sh validate RIMWORLD_MOD_DIR PROJECT_PATH MOD_FILE_NAME
  rimworld-deploy-guard.sh deploy RIMWORLD_MOD_DIR PROJECT_PATH CONFIGURATION PLATFORM MOD_FILE_NAME [BUILD_BRIDGE_TOOLS]

Validates the deploy root and serializes the destructive RimWorld deploy copy
against the real Mods directory, so symlink aliases cannot race each other.
USAGE
	exit 64
}

logical_dir() {
	local path="$1"
	[[ -d "$path" ]] || {
		printf 'RimWorld deploy root does not exist: %s\n' "$path" >&2
		exit 2
	}
	(cd -L "$path" && pwd -L)
}

physical_dir() {
	local path="$1"
	[[ -d "$path" ]] || {
		printf 'RimWorld deploy root does not exist: %s\n' "$path" >&2
		exit 2
	}
	(cd -P "$path" && pwd -P)
}

validate_mod_dir() {
	local rimworld_mod_dir="$1"
	local mod_file_name="$2"
	local provided_abs resolved_abs parent parent_name app_mods app_resolved

	provided_abs="$(logical_dir "$rimworld_mod_dir")"
	resolved_abs="$(physical_dir "$rimworld_mod_dir")"
	parent="$(dirname "$provided_abs")"
	parent_name="$(basename "$parent")"

	if [[ "$parent_name" == *-UserData ]]; then
		app_mods="${parent%-UserData}.app/Mods"
		if [[ -L "$app_mods" && -d "$app_mods" ]]; then
			app_resolved="$(physical_dir "$app_mods")"
			if [[ "$app_resolved" == "$resolved_abs" ]]; then
				cat >&2 <<EOF
Refusing RimWorld deploy through the UserData Mods path:
  $provided_abs

Use the paired app Mods symlink instead:
  $app_mods

That path deploys $mod_file_name to the same physical Mods folder while keeping
the derived sibling BridgeTools directory paired with the RimWorld app that
loads it.
EOF
				exit 2
			fi
		fi
	fi
}

lock_key_for() {
	local resolved_abs="$1"
	local mod_file_name="$2"

	if command -v shasum >/dev/null 2>&1; then
		printf '%s\n%s\n' "$resolved_abs" "$mod_file_name" | shasum -a 256 | awk '{ print $1 }'
	else
		printf '%s\n%s\n' "$resolved_abs" "$mod_file_name" | cksum | awk '{ print $1 "-" $2 }'
	fi
}

run_inner_deploy() {
	local rimworld_mod_dir="$1"
	local project_path="$2"
	local configuration="$3"
	local platform="$4"
	local mod_file_name="$5"
	local build_bridge_tools="${6:-}"
	local args=(
		"$project_path"
		-nologo
		-v:q
		-clp:ErrorsOnly
		"/t:_CopyToRimworldUnlocked;_ZipModUnlocked"
		"/p:RIMWORLD_MOD_DIR=$rimworld_mod_dir"
		"/p:Configuration=$configuration"
		"/p:Platform=$platform"
		"/p:RimworldDeployGuardInner=true"
	)

	if [[ -n "$build_bridge_tools" ]]; then
		args+=("/p:BuildBridgeTools=$build_bridge_tools")
	fi

	printf 'Deploying %s to %s\n' "$mod_file_name" "$rimworld_mod_dir" >&2
	dotnet msbuild "${args[@]}"
}

deploy_with_lock() {
	local rimworld_mod_dir="$1"
	local project_path="$2"
	local configuration="$3"
	local platform="$4"
	local mod_file_name="$5"
	local build_bridge_tools="${6:-}"
	local project_dir repo_root resolved_abs lock_root key lock_file lock_dir

	validate_mod_dir "$rimworld_mod_dir" "$mod_file_name"

	project_dir="$(cd "$(dirname "$project_path")" && pwd -P)"
	repo_root="$(cd "$project_dir/.." && pwd -P)"
	resolved_abs="$(physical_dir "$rimworld_mod_dir")"
	lock_root="$repo_root/obj/deploy-locks"
	mkdir -p "$lock_root"
	key="$(lock_key_for "$resolved_abs" "$mod_file_name")"
	lock_file="$lock_root/$key.lock"

	if command -v lockf >/dev/null 2>&1; then
		touch "$lock_file"
		lockf -t 0 "$lock_file" "$0" deploy-inner "$rimworld_mod_dir" "$project_path" "$configuration" "$platform" "$mod_file_name" "$build_bridge_tools"
		return
	fi

	lock_dir="$lock_file.dir"
	if mkdir "$lock_dir" 2>/dev/null; then
		trap 'rm -rf "$lock_dir"' EXIT
		run_inner_deploy "$rimworld_mod_dir" "$project_path" "$configuration" "$platform" "$mod_file_name" "$build_bridge_tools"
		return
	fi

	cat >&2 <<EOF
Refusing concurrent RimWorld deploy to:
  $resolved_abs

Another deploy for $mod_file_name is already holding:
  $lock_dir
EOF
	exit 3
}

mode="${1:-}"
case "$mode" in
	validate)
		(($# == 4)) || usage
		validate_mod_dir "$2" "$4"
		;;
	deploy)
		(($# == 6 || $# == 7)) || usage
		deploy_with_lock "$2" "$3" "$4" "$5" "$6" "${7:-}"
		;;
	deploy-inner)
		(($# == 6 || $# == 7)) || usage
		run_inner_deploy "$2" "$3" "$4" "$5" "$6" "${7:-}"
		;;
	*)
		usage
		;;
esac
