#!/usr/bin/env python3
"""Check the deploy guard's Mods path and running-game rules without a build."""

from pathlib import Path
import subprocess
import tempfile
import time


root = Path(__file__).resolve().parent.parent
guard = root / "scripts/rimworld-deploy-guard.sh"
project = root / "Source/ZombieLand.csproj"


def guard_run(*args):
    return subprocess.run([str(guard), *args], capture_output=True, text=True)


with tempfile.TemporaryDirectory(prefix="zl-deploy-guard-") as temp:
    install = Path(temp) / "RimWorld"
    app_mods = install / "RimWorldMac.app/Mods"
    app_mods.mkdir(parents=True)
    (install / "Mods").symlink_to("RimWorldMac.app/Mods", target_is_directory=True)
    plain = Path(temp) / "Plain/Mods"
    plain.mkdir(parents=True)

    for accepted in (install / "Mods", plain):
        result = guard_run("validate", str(accepted), str(project), "ZombieLand")
        assert result.returncode == 0, f"Rejected {accepted}: {result.stderr}"
    result = guard_run("validate", str(app_mods), str(project), "ZombieLand")
    assert result.returncode != 0 and "targets RimWorld 1.7" in result.stderr, result.stderr

    # A process named like the 1.7 executable must block deployment.
    result = guard_run("check-stopped")
    assert result.returncode == 0, f"Stop the running game first: {result.stderr}"
    fake = Path(temp) / "Game/RimWorld.app/Contents/MacOS/RimWorld"
    fake.parent.mkdir(parents=True)
    # macOS kills copied platform binaries. A symlink keeps its own path in ps.
    fake.symlink_to("/bin/sleep")
    process = subprocess.Popen([str(fake), "30"])
    try:
        for _ in range(20):
            if str(fake) in subprocess.run(["ps", "-o", "comm=", "-p", str(process.pid)], capture_output=True, text=True).stdout:
                break
            time.sleep(0.1)
        result = guard_run("check-stopped")
        assert result.returncode != 0 and "still running" in result.stderr, result.stderr
    finally:
        process.kill()
        process.wait()
print("ok")
