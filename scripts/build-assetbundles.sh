#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/Originals/Effects"
UNITY="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"
LOG="$PROJECT/Logs/zombieland-assetbundle-build.log"

usage() {
	cat <<'USAGE'
Usage: scripts/build-assetbundles.sh [--full | --current | --os Win64|Linux|MacOS] [--unity PATH]

Options:
  --full             Rebuild Win64, Linux, and MacOS bundles. Default.
  --current, --quick Rebuild only the bundle for the current machine.
  --os OS            Rebuild one explicit bundle: Win64, Linux, or MacOS.
  --unity PATH       Use a specific Unity editor executable.

The output bundle path is always 1.6/Resources/{OS}/zombieland.
USAGE
}

normalize_arch() {
	local arch
	arch="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
	case "$arch" in
		win|win64|windows|windows64)
			printf 'Win64'
			;;
		linux|linux64)
			printf 'Linux'
			;;
		mac|macos|osx|darwin)
			printf 'MacOS'
			;;
		*)
			printf 'Unknown architecture: %s\n' "$1" >&2
			exit 1
			;;
	esac
}

current_arch() {
	case "$(uname -s)" in
		Darwin)
			printf 'MacOS'
			;;
		Linux)
			printf 'Linux'
			;;
		MINGW*|MSYS*|CYGWIN*)
			printf 'Win64'
			;;
		*)
			printf 'Unsupported current machine OS: %s\n' "$(uname -s)" >&2
			exit 1
			;;
	esac
}

mode="full"
while (($#)); do
	case "$1" in
		--full)
			mode="full"
			shift
			;;
		--current|--quick)
			mode="current"
			shift
			;;
		--os)
			if (($# < 2)); then
				usage >&2
				exit 1
			fi
			mode="$(normalize_arch "$2")"
			shift 2
			;;
		--os=*)
			mode="$(normalize_arch "${1#--os=}")"
			shift
			;;
		--unity)
			if (($# < 2)); then
				usage >&2
				exit 1
			fi
			UNITY="$2"
			shift 2
			;;
		--unity=*)
			UNITY="${1#--unity=}"
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		*)
			printf 'Unknown option: %s\n' "$1" >&2
			usage >&2
			exit 1
			;;
	esac
done

case "$mode" in
	full)
		METHOD="CreateAssetBundles.BuildStandaloneAssetBundles"
		expected_arches=("Win64" "Linux" "MacOS")
		;;
	current)
		METHOD="CreateAssetBundles.BuildCurrentMachineAssetBundle"
		expected_arches=("$(current_arch)")
		;;
	Win64|Linux|MacOS)
		METHOD="CreateAssetBundles.Build${mode}AssetBundle"
		expected_arches=("$mode")
		;;
esac

if [[ ! -x "$UNITY" ]]; then
	printf 'Unity editor not found or not executable: %s\n' "$UNITY" >&2
	printf 'Set UNITY_EDITOR=/path/to/Unity.app/Contents/MacOS/Unity to override.\n' >&2
	exit 1
fi

if [[ "$(uname -m)" == "arm64" ]]; then
	if ! arch -x86_64 /usr/bin/true >/dev/null 2>&1; then
		printf 'Rosetta is required because Unity 2022.3.62f3 uses an x86_64 UnityPackageManager helper.\n' >&2
		printf 'Install it with: softwareupdate --install-rosetta --agree-to-license\n' >&2
		exit 1
	fi
fi

if [[ -f "$PROJECT/Temp/UnityLockfile" ]]; then
	if lsof "$PROJECT/Temp/UnityLockfile" >/dev/null 2>&1; then
		printf 'Unity project is locked by a running process: %s\n' "$PROJECT/Temp/UnityLockfile" >&2
		exit 2
	fi
	rm -f "$PROJECT/Temp/UnityLockfile"
fi

mkdir -p "$PROJECT/Logs"

printf 'Building Zombieland asset bundle(s): %s\n' "${expected_arches[*]}"

ZOMBIELAND_RESOURCES_DIR="$ROOT/1.6/Resources" "$UNITY" \
	-batchmode \
	-quit \
	-nographics \
	-projectPath "$PROJECT" \
	-acceptSoftwareTermsForThisRunOnly \
	-executeMethod "$METHOD" \
	-logFile "$LOG"

if command -v rg >/dev/null 2>&1; then
	if rg -n "Shader error in" "$LOG"; then
		printf 'Unity reported shader compiler errors; refusing to accept the generated bundle.\n' >&2
		exit 1
	fi
elif grep -n "Shader error in" "$LOG"; then
	printf 'Unity reported shader compiler errors; refusing to accept the generated bundle.\n' >&2
	exit 1
fi

for arch in "${expected_arches[@]}"; do
	bundle="$ROOT/1.6/Resources/$arch/zombieland"
	if [[ ! -f "$bundle" ]]; then
		printf 'Missing expected bundle: %s\n' "$bundle" >&2
		exit 1
	fi

	if command -v rg >/dev/null 2>&1; then
		rg -n "Zombieland bundle validated $arch:" "$LOG"
	else
		grep -n "Zombieland bundle validated $arch:" "$LOG"
	fi
	shasum -a 256 "$bundle"
	ls -l "$bundle"
done

if command -v rg >/dev/null 2>&1; then
	rg -n "Exiting batchmode successfully" "$LOG"
else
	grep -n "Exiting batchmode successfully" "$LOG"
fi
