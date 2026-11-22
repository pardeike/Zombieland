#!/usr/bin/env python3
"""Exercise the normal deployment guard against disposable Mods folders.

Run after a local Release build. Pass the published legacy root, for example the
Workshop 928376710 folder. The run rebuilds artifacts/package and the ZIP."""

from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile


root = Path(__file__).resolve().parent.parent
baseline = Path(sys.argv[1]).resolve(strict=True)
log = root / "artifacts/logs/deploy-preservation.log"


def snapshot(folder):
    result = {}
    for path in sorted(folder.rglob("*")):
        stat = path.stat()
        result[str(path.relative_to(folder))] = (
            stat.st_ino, stat.st_mtime_ns,
            hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None,
        )
    return result


with tempfile.TemporaryDirectory(prefix="deploy-preservation-", dir=root / "artifacts") as temp:
    directory = Path(temp)
    mods = directory / "Mods"
    installed = mods / "ZombieLand"
    shutil.copytree(baseline, installed)
    note = installed / "local-notes.txt"
    note.write_text("Unrelated installed state must survive.\n")
    (installed / "1.7").mkdir()
    stale = installed / "1.7/stale-file.txt"
    stale.write_text("A stale 1.7 file must disappear.\n")
    before = {version: snapshot(installed / version) for version in ("1.4", "1.6")}
    owned = [path.relative_to(root).as_posix() for path in (root / "About").rglob("*")
             if path.is_file() and path.name != ".DS_Store"] + ["LoadFolders.xml", "README.md", "LICENSE"]
    # Make the installed README stale and remove one About file. Deployment must restore both from the repository.
    (installed / "README.md").write_bytes((installed / "README.md").read_bytes() + b"older shared text")
    (installed / "About/ModIcon.png").unlink()
    notes_before = {path: (path.stat().st_ino, path.stat().st_mtime_ns, path.read_bytes()) for path in (note,)}
    environment = {**os.environ, "ZOMBIELAND_LEGACY_MOD_ROOT": str(baseline), "RIMWORLD_MOD_DIR": str(mods)}
    command = [str(root / "scripts/rimworld-deploy-guard.sh"), "deploy", str(mods),
               str(root / "Source/ZombieLand.csproj"), "Release", "AnyCPU", "ZombieLand", "true"]
    with log.open("w") as output:
        result = subprocess.run(command, cwd=root, env=environment, stdout=output, stderr=subprocess.STDOUT)
    assert result.returncode == 0, f"Normal deployment failed. Read {log}"
    for version in before:
        assert snapshot(installed / version) == before[version], f"Frozen {version} inode, mtime or bytes changed"
    for name in owned:
        assert (installed / name).read_bytes() == (root / name).read_bytes(), f"Repository-owned file not replaced: {name}"
    for path, state in notes_before.items():
        assert (path.stat().st_ino, path.stat().st_mtime_ns, path.read_bytes()) == state
    assert not stale.exists(), "Stale installed 1.7 file survived the mirror"
    stage = root / "artifacts/package/ZombieLand/1.7"
    assert sorted(p.relative_to(stage) for p in stage.rglob("*")) == sorted(p.relative_to(installed / "1.7") for p in (installed / "1.7").rglob("*"))
    with zipfile.ZipFile(mods / "ZombieLand.zip") as package:
        assert not any(name.endswith("local-notes.txt") for name in package.namelist())
    for source, destination in (
        (root / "1.7/Assemblies/ZombieLand.dll", installed / "1.7/Assemblies/ZombieLand.dll"),
        (root / "artifacts/BridgeTools/Zombieland/Zombieland.BridgeTools.dll", directory / "BridgeTools/Zombieland/Zombieland.BridgeTools.dll"),
    ):
        assert source.read_bytes() == destination.read_bytes()
    changed = installed / "1.6/Assemblies/ZombieLand.dll"
    changed.write_bytes(changed.read_bytes() + b"older local build")
    extra = installed / "1.6/local-legacy-file.txt"
    extra.write_text("Preserve historical extras too.\n")
    preserved = {path: (path.stat().st_ino, path.stat().st_mtime_ns, path.read_bytes())
                 for path in (changed, extra)}
    with log.open("a") as output:
        result = subprocess.run(command, cwd=root, env=environment, stdout=output, stderr=subprocess.STDOUT)
    assert result.returncode == 0, f"Complete differing legacy payload was rejected. Read {log}"
    for path, state in preserved.items():
        assert (path.stat().st_ino, path.stat().st_mtime_ns, path.read_bytes()) == state
    with zipfile.ZipFile(mods / "ZombieLand.zip") as package:
        assert package.read("1.6/Assemblies/ZombieLand.dll") == (baseline / "1.6/Assemblies/ZombieLand.dll").read_bytes()
        assert package.read("README.md") == (root / "README.md").read_bytes()
        assert "1.6/local-legacy-file.txt" not in package.namelist()
    changed.unlink()
    incomplete = snapshot(directory)
    with log.open("a") as output:
        result = subprocess.run(command, cwd=root, env=environment, stdout=output, stderr=subprocess.STDOUT)
    assert result.returncode != 0, "Deployment accepted an incomplete frozen payload"
    assert snapshot(directory) == incomplete, "Rejected deployment changed installed files"
    assert "missing required files" in log.read_text()
    result = {"frozenEntries": sum(len(items) for items in before.values()),
              "matchingInodesMtimesAndBytesPreserved": True,
              "unrelatedFilesPreserved": True, "stale17Removed": True, "repositoryOwnedFilesReplaced": True, "differingLegacyPreserved": True, "incompleteRejectedBeforeInstalledWrites": True,
              "packageUsesPublishedBaseline": True, "packageUsesRepositoryReadme": True,
              "pairedDllsVerified": True, "zipExcludesUnrelatedFiles": True}
    (root / "artifacts/1.7-port/deploy-preservation.json").write_text(json.dumps(result, indent=2) + "\n")
print("ok")
