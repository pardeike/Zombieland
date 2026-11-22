#!/usr/bin/env python3
"""Install the staged package. Frozen installed 1.4/1.6 payloads stay untouched."""

from pathlib import Path
import hashlib
import shutil
import sys


FROZEN = ("1.4", "1.6")
ACTIVE = "1.7"


def ignored(path):
    return path.name == ".DS_Store" or path.name.startswith("._") or path.suffix in {".pdb", ".mdb"}


def names(folder):
    result = set()
    for path in folder.rglob("*"):
        if path.is_symlink():
            raise ValueError(f"Payload contains a symlink: {path}")
        if path.is_file() and not ignored(path):
            result.add(path.relative_to(folder).as_posix())
    return result


def hashes(folder):
    return {name: hashlib.sha256((folder / name).read_bytes()).hexdigest() for name in names(folder)}


def owned_files(stage):
    """Repository-owned files outside the version folders: About/, LoadFolders.xml, README.md, LICENSE."""
    return sorted(name for name in names(stage) if name.split("/")[0] not in {*FROZEN, ACTIVE})


def validate(stage, destination):
    if destination.is_symlink():
        raise ValueError(f"Refusing a symlinked mod destination: {destination}")
    resolved = destination.resolve()
    if resolved == stage or resolved.is_relative_to(stage) or stage.is_relative_to(resolved):
        raise ValueError("The deployment destination overlaps the package stage")
    if not destination.exists():
        return
    # Require a complete installation, then preserve its frozen files in place.
    # Older local payloads may differ. Only new installations copy the frozen payload.
    for version in FROZEN:
        installed = destination / version
        if installed.is_symlink() or not installed.is_dir():
            raise ValueError(f"Installed frozen payload is missing or symlinked: {installed}")
        missing = sorted(names(stage / version) - names(installed))
        if missing:
            raise ValueError(f"Installed frozen {version} is missing required files: {', '.join(missing[:8])}")
    for name in (ACTIVE, "About", *owned_files(stage)):
        target = destination / name
        if target.is_symlink() or (name not in (ACTIVE, "About") and target.is_dir()):
            raise ValueError(f"Refusing a symlinked or non-file installed path: {target}")


def install(stage, destination):
    stage = stage.resolve(strict=True)
    validate(stage, destination)
    if not destination.exists():
        shutil.copytree(stage, destination, copy_function=shutil.copy2)
    else:
        # Mirror the active payload so stale installed 1.7 files disappear.
        if (destination / ACTIVE).exists():
            shutil.rmtree(destination / ACTIVE)
        shutil.copytree(stage / ACTIVE, destination / ACTIVE, copy_function=shutil.copy2)
        for name in owned_files(stage):
            (destination / name).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(stage / name, destination / name)
    if hashes(destination / ACTIVE) != hashes(stage / ACTIVE):
        raise ValueError(f"Installed {ACTIVE} differs from the package stage")
    for name in owned_files(stage):
        if (destination / name).read_bytes() != (stage / name).read_bytes():
            raise ValueError(f"Installed {name} differs from the package stage")


if __name__ == "__main__":
    try:
        install(Path(sys.argv[1]), Path(sys.argv[2]))
    except (OSError, ValueError) as error:
        print(f"error : {error}", file=sys.stderr)
        raise SystemExit(2)
