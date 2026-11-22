#!/usr/bin/env python3
"""Check priority composition against real game, Harmony, and Achtung assemblies."""

from pathlib import Path
from xml.sax.saxutils import escape
import os
import subprocess
import sys


root = Path(__file__).resolve().parent.parent
work = root / "artifacts/drafted-double-tap-test"
log = root / "artifacts/logs/drafted-double-tap-test.log"
step = "prerequisites"
try:
    if len(sys.argv) != 4:
        raise ValueError("Usage: test-drafted-double-tap.py GAME_MACOS_DIR HARMONY_DLL ACHTUNG_DLL")
    game, harmony, achtung = (Path(arg).resolve(strict=True) for arg in sys.argv[1:])
    references = [game / "Assembly-CSharp.dll", game / "UnityStubs.dll",
                  harmony, root / "1.7/Assemblies/ZombieLand.dll"]
    for path in references:
        if not path.is_file():
            raise ValueError(f"Missing assembly: {path}")
    work.mkdir(parents=True, exist_ok=True)
    log.parent.mkdir(parents=True, exist_ok=True)
    items = "".join(f'<Reference Include="{escape(p.stem)}"><HintPath>{escape(str(p))}</HintPath>'
                    '<Private>false</Private></Reference>' for p in references)
    sources = [root / "Source/DraftedDoubleTap.cs", root / "scripts/tests/DraftedDoubleTap.cs"]
    items += "".join(f'<Compile Include="{escape(str(p))}"/>' for p in sources)
    project = work / "DraftedDoubleTapTest.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                       '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                       '<TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
                       '</PropertyGroup><ItemGroup>' + items + '</ItemGroup></Project>\n')
    environment = {**os.environ, "RIMWORLD_MOD_DIR": ""}
    with log.open("w") as output:
        step = "test build"
        subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo", "-v:q"],
                       cwd=root, env=environment, stdout=output, stderr=subprocess.STDOUT, check=True)
        step = "priority regression"
        subprocess.run(["dotnet", str(work / "bin/Release/net10.0/DraftedDoubleTapTest.dll"),
                        str(game), str(harmony.parent), str(achtung), str(references[-1].parent)],
                       cwd=work, env=environment, stdout=output, stderr=subprocess.STDOUT, check=True)
except (ValueError, OSError, subprocess.CalledProcessError) as error:
    print(f"{step} failed: {error}. Full log: {log}", file=sys.stderr)
    if log.exists():
        print("\n".join(log.read_text().splitlines()[-16:]), file=sys.stderr)
    sys.exit(1)
print("ok")
