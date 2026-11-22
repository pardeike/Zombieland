#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
mkdir -p "$ROOT/artifacts/logs"
build_log="$ROOT/artifacts/logs/build-quiet.log"
exec 3>&1 4>&2
exec >"$build_log" 2>&1
build_step="format"
build_pid=""
finish_build() {
	local status=$?
	if ((status == 0)); then
		printf 'ok\n' >&3
	else
		printf '%s failed (exit %s). Full log: %s\n' "$build_step" "$status" "$build_log" >&4
		tail -n 20 "$build_log" >&4
	fi
}
cancel_build() {
	if [[ -n "$build_pid" ]]; then
		kill -TERM "$build_pid" 2>/dev/null || true
		wait "$build_pid" 2>/dev/null || true
	fi
	exit "$1"
}
run_build_step() {
	"$@" &
	build_pid=$!
	wait "$build_pid"
	build_pid=""
}
trap finish_build EXIT
trap 'cancel_build 130' INT
trap 'cancel_build 143' TERM

build_step="deployment guard"

local_build=false
package_mod=false
build_bridge_tools=true
configuration_given=false
dotnet_args=()
rimworld_mod_dir_arg=""
for arg in "$@"; do
	case "$arg" in
		--local)
			local_build=true
			;;
		--package)
			package_mod=true
			;;
		--legacy-root=*)
			export ZOMBIELAND_LEGACY_MOD_ROOT="${arg#*=}"
			;;
		/p:RIMWORLD_MOD_DIR=*|-p:RIMWORLD_MOD_DIR=*)
			rimworld_mod_dir_arg="${arg#*=}"
			dotnet_args+=("$arg")
			;;
		*)
			case "$arg" in
				-c|--configuration|-c:*|--configuration:*|-c=*|--configuration=*|[-/]p:Configuration=*|[-/]property:Configuration=*)
					configuration_given=true
					;;
				*BuildBridgeTools=false*)
					build_bridge_tools=false
					;;
			esac
			dotnet_args+=("$arg")
			;;
	esac
done
if [[ "$configuration_given" == false ]]; then
	dotnet_args+=(-c Release)
fi

if [[ "$local_build" == true ]]; then
	if [[ -n "$rimworld_mod_dir_arg" ]]; then
		printf '%s\n' '--local cannot deploy.' >&2
		exit 2
	fi
	export RIMWORLD_MOD_DIR=
	dotnet_args+=("-p:RIMWORLD_MOD_DIR=")
fi

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

# A deploy always builds the ZIP. Packaging alone needs the explicit target.
if [[ "$package_mod" == true && -z "$effective_rimworld_mod_dir" ]]; then
	dotnet_args+=("-t:PackageMod")
fi

companion_state() {
	stat -f '%i %m %z' "$(dirname "${effective_rimworld_mod_dir%/}")/BridgeTools/Zombieland/Zombieland.BridgeTools.dll" 2>/dev/null || printf 'missing\n'
}

# Deploys refuse a running game and never start or stop RimWorld. The guard
# repeats this check inside its deploy lock.
if [[ -n "$effective_rimworld_mod_dir" ]]; then
	"$ROOT/scripts/rimworld-deploy-guard.sh" check-stopped
	companion_before="$(companion_state)"
fi

if [[ "$package_mod" == true || -n "$effective_rimworld_mod_dir" ]]; then
	build_step="legacy baseline validation"
	python3 - "$effective_rimworld_mod_dir" <<'PY'
from pathlib import Path
import hashlib
import json
import os
import sys
import xml.etree.ElementTree as ET

value = os.environ.get('ZOMBIELAND_LEGACY_MOD_ROOT')
if not value:
    raise SystemExit('Select --legacy-root=PATH. Repository and Steam 1.6 resources differ.')
legacy = Path(value).resolve()
root = Path.cwd()
if legacy == root or legacy.is_relative_to(root / 'artifacts'):
    raise SystemExit('Use the preserved installed/released baseline, not the working checkout or artifacts.')
if sys.argv[1]:
    target = (Path(sys.argv[1]) / 'ZombieLand').resolve()
    if legacy == target or legacy.is_relative_to(target):
        raise SystemExit('The legacy baseline cannot be inside the deployment destination.')
if ET.parse(legacy / 'About/About.xml').findtext('packageId') != 'brrainz.zombieland':
    raise SystemExit('The selected legacy baseline is not Zombieland.')
old_load = ET.parse(legacy / 'LoadFolders.xml')
new_load = ET.parse(root / 'LoadFolders.xml')
pin = json.loads((root / 'scripts/published-baseline.json').read_text())
actual = {}
for version in ('1.4', '1.6'):
    if [x.text for x in old_load.findall(f'v{version}/li')] != [x.text for x in new_load.findall(f'v{version}/li')]:
        raise SystemExit(f'Legacy load folders differ for {version}. Preserve their lookup order before packaging.')
    for path in (legacy / version).rglob('*'):
        if path.is_file() and path.name != '.DS_Store' and not path.name.startswith('._') and path.suffix not in {'.pdb', '.mdb'}:
            actual[path.relative_to(legacy).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
if actual != pin['files']:
    changed = sorted(name for name in pin['files'].keys() | actual.keys() if pin['files'].get(name) != actual.get(name))
    raise SystemExit(f'{legacy} does not match scripts/published-baseline.json ({pin["source"]}): {", ".join(changed[:8])}')
PY
fi

build_step="format"
run_build_step "$ROOT/scripts/format-project.sh"

# Deploy builds intentionally use one root: RIMWORLD_MOD_DIR/../BridgeTools is
# the paired global BridgeTools location for that RimWorld Mods folder.
build_step="build/deploy"
run_build_step dotnet build Source/ZombieLand.csproj -v:q -clp:ErrorsOnly "${dotnet_args[@]}"
if [[ -n "$effective_rimworld_mod_dir" ]]; then
	build_step="paired deployment verification"
	cmp "${ROOT}/1.7/Assemblies/ZombieLand.dll" "$effective_rimworld_mod_dir/ZombieLand/1.7/Assemblies/ZombieLand.dll"
	cmp "$ROOT/artifacts/ZombieLand.zip" "$effective_rimworld_mod_dir/ZombieLand.zip"
	if [[ "$build_bridge_tools" == true ]]; then
		cmp "$ROOT/artifacts/BridgeTools/Zombieland/Zombieland.BridgeTools.dll" "$(dirname "${effective_rimworld_mod_dir%/}")/BridgeTools/Zombieland/Zombieland.BridgeTools.dll"
	elif [[ "$(companion_state)" != "$companion_before" ]]; then
		printf '%s\n' 'A -p:BuildBridgeTools=false deploy changed the installed companion.' >&2
		exit 1
	fi
fi
if [[ "$package_mod" == true || -n "$effective_rimworld_mod_dir" ]]; then
	build_step="package verification"
	python3 - <<'PY'
from pathlib import Path
import hashlib
import json
import zipfile

root = Path.cwd()
pin = json.loads((root / 'scripts/published-baseline.json').read_text())['files']

def excluded(path):
    return (path.name in {'.DS_Store', '0Harmony.dll'} or path.name.startswith('._')
            or path.suffix in {'.pdb', '.mdb'} or path.name.endswith('.deps.json')
            or 'BridgeTools' in path.parts or path.name.startswith('RimBridgeServer'))

# Frozen 1.4/1.6 come from the pinned published baseline. Everything else comes from the checkout.
expected = set(pin)
for folder in ('1.7', 'About'):
    expected.update(path.relative_to(root).as_posix() for path in (root / folder).rglob('*')
                    if path.is_file() and not excluded(path))
expected.update(('LoadFolders.xml', 'LICENSE', 'README.md'))
with zipfile.ZipFile(root / 'artifacts/ZombieLand.zip') as package:
    names = {entry.filename for entry in package.infolist() if not entry.is_dir()}
    if names != expected:
        raise SystemExit(f'Package file set differs: missing={sorted(expected - names)}, extra={sorted(names - expected)}')
    for required in ('About/About.xml', 'LoadFolders.xml', '1.4/Assemblies/ZombieLand.dll',
                     '1.6/Assemblies/ZombieLand.dll', '1.7/Assemblies/ZombieLand.dll',
                     '1.7/Assemblies/CrossPromotion.dll', '1.7/Assemblies/Newtonsoft.Json.dll'):
        if required not in names:
            raise SystemExit(f'Package is missing {required}')
    for name in names:
        if excluded(Path(name)):
            raise SystemExit(f'Package contains a development file: {name}')
        data = package.read(name)
        if name in pin:
            if hashlib.sha256(data).hexdigest() != pin[name]:
                raise SystemExit(f'Package differs from scripts/published-baseline.json: {name}')
        elif data != (root / name).read_bytes():
            raise SystemExit(f'Package differs from current source: {name}')
PY
fi
