# Scripts

## Project Formatting and Build

Use `./scripts/build-quiet.sh --local` for the normal local compile cycle. The wrapper builds Release unless the arguments name a configuration. Add `--package --legacy-root=/path/to/published/Zombieland` to create and verify `artifacts/ZombieLand.zip`. The command builds the main mod and its companion. The `--local` option clears inherited deployment settings.

The current projects target RimWorld 1.7 with .NET 10 and AnyCPU. They use `Krafs.Rimworld.Ref` 1.7.5014-unstable (game build 1.7.5014), `Lib.Harmony.Ref` 3.0.0-rc.7 (the `Lib.Harmony` build in the Harmony mod 3.0.4.0), `Newtonsoft.Json` 13.0.4, `Newtonsoft.Json.Bson` 1.0.3, and development-only `RimBridgeServer.Sdk` 2.0.0. `TaskPubliciser` 1.0.3 and CrossPromotion 1.1.2 are the latest stable upstream packages. CrossPromotion 1.1.2 only provides a .NET Framework assembly. Its scoped NU1701 suppression retains that helper until runtime compatibility is verified.

The companion restores published `RimBridgeServer.Sdk` 2.0.0 from NuGet.org. The root `NuGet.Config` restricts restore to NuGet.org, so no local SDK feed or package build is used. The companion excludes the SDK from runtime output and the public mod package.

Current gameplay output is `1.7/Assemblies/ZombieLand.dll`. Companion output is `artifacts/BridgeTools/Zombieland/Zombieland.BridgeTools.dll`. Local packages contain isolated 1.4, 1.6, and 1.7 payloads. The explicit legacy root supplies only the frozen 1.4 and 1.6 files. The working checkout supplies `1.7/`, `About/`, `LoadFolders.xml`, `README.md`, and `LICENSE`. The repository and installed Steam 1.6 resources differ in 30 files. Packaging must use the published baseline rather than silently substitute repository resources. Packages exclude companions, SDK DLLs, private Harmony, debug symbols, dependency manifests, and Finder metadata. Packaging does not deploy or publish.

`published-baseline.json` pins the published baseline. It holds the SHA-256 of every 1.4 and 1.6 file of Workshop item 928376710 at version 5.7.2.0. The Workshop folder `steamapps/workshop/content/294100/928376710` holds that payload. Before compiling, the wrapper requires the legacy root's 1.4 and 1.6 files to match the pin exactly. It refuses the working checkout and `artifacts/` as the legacy root. It also checks that the legacy load-folder lookup order is unchanged. Package verification checks every legacy entry against the pin and every other entry against the checkout. Replace the pin only when a new published release changes the frozen payload.

For deployment, omit `--local` and set `RIMWORLD_MOD_DIR` to the Mods folder of a RimWorld 1.7 installation. On macOS, use the outer `<RimWorld install>/Mods`, even when it is a symlink into `RimWorldMac.app`. The guard rejects the `RimWorldMac.app/Mods` alias because the companion would go to the BridgeTools folder of the 1.6 Unity build. Supply the same `--legacy-root=PATH`. A baseline inside the deployment destination is rejected. Deployment refuses a running RimWorld 1.6 or 1.7 process, once before the build and again inside the deploy lock. The wrapper never starts or stops RimWorld. Use GABS for that.

A deploy builds the package stage `artifacts/package/ZombieLand` and the ZIP. Then `install-package.py` installs the stage. It first checks that both installed frozen folders are complete. Missing frozen files or symlinks stop deployment before any installed file changes. Complete frozen 1.4 and 1.6 folders are never copied, removed, or rewritten, even if their bytes differ from the published baseline. A fresh installation copies the whole stage. The installer replaces the installed `1.7/` with the staged one, so stale 1.7 files disappear. It also replaces the repository-owned files: everything under `About/`, `LoadFolders.xml`, `README.md`, and `LICENSE`. It then verifies that the installed `1.7/` and these files equal the stage. Other installed files outside `1.7/` stay in place. The wrapper then verifies the ZIP and compares the installed ZIP, gameplay DLL, and companion with the build. With `-p:BuildBridgeTools=false`, it skips the companion comparison and fails if the installed companion changed.

Before compiling, the command runs `./scripts/format-project.sh`, which applies the repository `.editorconfig` to all C# in both `Source/ZombieLand.csproj` and `Source/BridgeTools/Zombieland.BridgeTools.csproj`. It also formats only changed active XML-like files: runtime XML under `1.7/`, metadata under `About/`, `LoadFolders.xml`, `Directory.Build.props`, and hand-maintained `.csproj` files under `Source/`. The preserved `1.4/` and `1.6/` payloads and Unity sources under `Originals/` are intentionally excluded.

The build command saves full output to `artifacts/logs/build-quiet.log` and prints only `ok` after success. Failure names the step and log, returns nonzero, and stops dependent steps. Cancellation terminates the active formatting/build child.

The XML formatter requires `python3` and `xmllint`. It uses tab indentation, preserves each file's UTF-8 BOM and LF/CRLF style, adds a final newline, and compares canonical XML before replacing a file. Changed runtime XML also goes through `./scripts/check-versioned-xml.sh`. The formatter refuses the rewrite if parsing fails or the canonical document changes.

Run a read-only formatting check with:

```bash
./scripts/format-project.sh --check
```

Pass `--base GIT_REVISION` with `--check` to validate active XML changed since a committed revision. Before a release, run it locally with the previous version tag, then run `./scripts/check-versioned-xml.sh`. The tag base covers every XML change in the release candidate, including feature commits before a separate version-bump commit. CI does not build, format, or check XML. The release workflow only prepares the GitHub release.

The repository pins .NET SDK 10.0.400 through `global.json` with `rollForward: latestPatch`. The formatter accepts any 10.0.4xx SDK, the same range `global.json` allows. A normal non-deploy build compiles both the main mod and its BridgeTools companion. Pass `-p:BuildBridgeTools=false` only for a workflow that intentionally excludes the companion, such as the public mod-only release package.

## Deploy checks

Run `python3 scripts/test-deploy-guard.py` after changing the deploy guard. It needs no build and no game. It checks that the guard accepts an outer Mods symlink and a plain Mods folder, rejects the `RimWorldMac.app/Mods` alias, and refuses a stand-in process named like the 1.7 executable. It fails if RimWorld is already running.

Run `python3 scripts/test-deploy-preservation.py LEGACY_ROOT` after a local Release build, with the published legacy root. It deploys into disposable Mods folders under `artifacts/`. It proves that frozen files keep their inodes, modification times, and bytes, that stale 1.7 files disappear, that stale or missing repository-owned files are replaced, and that an incomplete frozen payload stops deployment before any write. It also checks that the ZIP uses the published baseline and the repository README. The run rebuilds `artifacts/package` and the ZIP. Full output goes to `artifacts/logs/deploy-preservation.log`.

## Drafted Double Tap compatibility regression

Run `python3 scripts/test-drafted-double-tap.py GAME_MACOS_DIR HARMONY_DLL ACHTUNG_DLL` with current RimWorld 1.7, Harmony, and Achtung assemblies. Build Zombieland locally first if `1.7/Assemblies/ZombieLand.dll` is absent. The check compiles the production transpiler source and runs the real Achtung transpiler over the game's menu IL. It verifies both installation orders, forced-menu decoration, anchor validation, instruction metadata, and a 32-row priority matrix. It loads assemblies without starting RimWorld or installing mods. This is a local .NET check. Combined-game validation remains necessary. Full output goes to `artifacts/logs/drafted-double-tap-test.log`.

## Thumper dust rendering

Use `zombieland/thumper_dust_render_sample` through RimBridgeServer on a disposable debug map. It pauses the game and freezes the production dust prefab at the requested particle age. Use `seconds: -1` to remove the sample and capture an empty baseline. Compare `seconds: 0.3, opacity: 0` against that baseline, then capture normal opacity at ages `0.05`, `0.3`, `0.6`, `0.85`, and `1.1`. The tool uses the same particle seed each time. These ages are particle simulation seconds, independent of gameplay speed. No terrain or pawns are changed.

For consistent framing, call `rimworld/screenshot_cell_rect` with `x: 17, z: 17, width: 76, height: 76, rootSize: 50, paddingCells: 0, doNotResetCamera: true`. Clear and tile cells `(15,15)` through `(95,95)` on a disposable map. Load with visual readiness and discard a warm-up capture before recording the empty baseline. Zero alpha must preserve the background. Normal opacity must remain visibly comparable to the 1.6 reference, expand, and fade. Nonzero pixel differences alone do not establish a visual match. The 1.1-second capture must return to the empty baseline. Finish with `seconds: -1`. Run the existing `zombieland/thumper_visual_wave_damage_contract` separately for fuel, timing, and radial damage. `TEST_SCENARIOS.md` records the evidence and engine limitations.

The 1.7 dust shader uses `Defs/ShaderDefs/ThumperDust.xml` and `Shaders/ZombielandThumperDust.frag.glsl`. Its prefab and texture still come from the bundle. Launch the isolated game with `-WatchShaders` to iterate on the deployed GLSL file. The engine reloads shader source in the same process. A ShaderDef or C# change still requires the normal build and restart. Copy accepted source changes back to the repository, then finish with a paired build, deployment, and fresh runtime check.

## XML Validation

Run `./scripts/check-versioned-xml.sh` after changing defs, patches, language data, `LoadFolders.xml`, mod metadata, or the settings dialog. The check parses every tracked or non-ignored project XML file, verifies the versioned runtime layout, rejects unexpected direct text in active container documents, detects empty or duplicate active translation entries, detects duplicate active def names, requires active non-English keyed files to have the same key set as English, derives every hover-help target used by the active settings dialog (including dynamic buttons, enum choices, and special sliders) and requires it in every active language, requires every active language to have the same DefInjected key set, verifies every DefInjected key targets a real English def and full field path (including RimWorld's stable named-list and quest-node paths), preserves runtime placeholders and formatting tokens, requires every direct English def label and description to be translated in every active non-English language, and rejects duplicate effective labels among active non-abstract ThingDefs after each language's injections are applied.

RimWorld's live translation report also lists inherited defaults that are not present in Zombieland's English XML. Do not add language-only keys for those false positives. The intentional exclusions are the inherited generic death messages for `ToxicSplatter` and `SeismicWave`, `Zombies.leaderTitle`, `ZombieBite.labelNounPretty`, and the default `Mote` labels on Zombieland's internal visual-only ThingDefs. Translation entries must continue to correspond to explicit English source fields.

The preserved `1.4/` and `1.6/` payloads are still checked for XML parse failures and layout isolation, but its known legacy translation structure is frozen and is not subjected to the newer active-language anomaly and parity rules.

## RimWorld Log Summary

Run `./scripts/summarize-rimworld-log.sh /path/to/Player.log` to collapse repeated error blocks before inspecting a runtime test. The summary includes exceptions plus early play-data failures such as `Config error in`, grammar-rule failures, and failed resolution messages; those can be emitted before RimBridge initializes and therefore must not be checked only through the live bridge journal.

## Incident save/load regressions

Use `incident-save-load-regression.lua` with disposable saves whose recorded mods match the active loadout. Compile it with `rimbridge/compile_lua_file`, then run it with `rimbridge/run_lua_file`. Pass `includeStepResults=false` for compact output. Each mode asserts its result and returns the incident state.

| Mode | Fixture requirements | Assertion |
| --- | --- | --- |
| `shortSessions` | Overdue incident, capable armed colonists, event-only spawning, constant threat, silence disabled, valid spawn ground. | Three saved 10,000-tick segments reach the incident check. |
| `failedPlacement` | Same overdue setup, all-over-map spawning, fully tiled ground, saved `daysStretched=2.5`. | A failed placement preserves the timestamp and interval. |
| `reloadDuringWave` | The valid overdue fixture. Start in a fresh process for a clean error journal. | Loading during generation produces no errors and leaves no active generation count. |

Supply `parameters={mode:..., saveName:...}`. The optional `scratchSave` parameter defaults to `ZL_Incident_Regression_scratch` and is overwritten by `shortSessions`. The maintained local fixtures are `ZL_Incident_LoadRegression_20261024_overdue` and `ZL_Incident_Regression_blocked`. `TEST_SCENARIOS.md` owns their red/green evidence. Fully tiled ground deliberately has no all-over-map spawn spots, so the placement case expects an incident-creation warning.

## Zombie Ticking Runtime Benchmark

`zombie-ticking-benchmark.lua` is the reusable real-RimBridge entry point for the adaptive scheduler regression and speed matrix. The lowered-Lua file is intentionally a thin parameter wrapper around `zombieland/zombie_ticking_benchmark`; the companion tool owns the sequence so its C# `finally` restores test mode even when a nested call fails or the operation is cancelled.

Always compile the file against the live bridge before executing it:

```text
rimbridge/compile_lua_file
  scriptPath=/Users/ap/Projects/Zombieland/scripts/zombie-ticking-benchmark.lua
```

For the disposable 25-zombie matrix, run it against `EMPTY` with `spawnCount=25`, the spawn centered at `(10,10)`, the camera at `(240,240)`, `durationMs=2500`, `forceRequestedSpeed=false`, and `runContracts=true`. This runs the adaptive-feedback, fair-scheduler, and path-payment contracts once, then reloads the save for independent Normal, Fast, Superfast, and Ultrafast samples. The companion uses `zombieland/zombie_ticking_test_mode` to suppress initial-grace, ordinary zero-threat and scheduled zombie-free cleanup during measurement. Its single-run gate and non-cancelled teardown restore the copied settings timeline on success, failure, or cancellation; `zombieland/zombie_ticking_benchmark_cleanup_contract` injects both failure modes through that same scope helper.

For the dense matrix, use `saveName=ZL_Dense_Performance_1500_base`, `spawnCount=0`, the camera at `(240,240)`, and `runContracts=false`. The save is reloaded before every speed so zombie deaths, controller history, and queue history from one sample cannot contaminate the next.

The result includes each `rimworld/play_for` report, before/after `zombieland/zombie_lightweight_perf_state` snapshots, active-mod configuration, contract output, and warning-or-higher logs. The important scheduler assertions are:

- no state reports the retired `remoteFrozen` behavior;
- remote work has a nonzero rate and bounded age even in `Emergency`;
- priority zombies are selected every preparation;
- the queue remains bounded across zombie destruction/replacement;
- `MoveSpeed` is independent of game and sampling speed, while `CostToPayThisTick` receives bounded compensation;
- Normal/Fast/Superfast/Ultrafast demand is compared after division by `TickRateMultiplier`.

Use `forceRequestedSpeed=false` for player-representative evidence. Set it to `true` only for a separate stress ceiling; that option suppresses forced slowdown and enables RimWorld's ultrafast debug boost while still using the normal `TickManagerUpdate` path.

### Predefined player-speed evidence fixtures

For a separate quiet horde comparison, set `minimumColonistDistance=60` and
place the camera cluster away from the colony. This uses the actual current
colonist positions, including before the manager's cached center has refreshed,
and applies the distance constraint to visible, remote and fallback candidates.
The default zero preserves existing combat fixture placement. The creation
result records the closest initial squared distance. Verify actual native
tick-rate multipliers during playback: selected Ultrafast can still run at
multiplier 1 when normal combat slowdown is active.

Use the async `zombieland/zombie_ticking_create_player_fixture` companion tool to create deterministic, reusable 250×250 player saves with production-random zombie types. Build 100, 500, 1,000, and 2,000 variants from the same clean base save, keep the default 0.25% chances for each special type, and give each population a distinct seed. The tool places 10% of the horde in the exact camera rectangle, the remainder outside the 12-cell camera-protection margin, sets initial grace to zero and disables ordinary/scheduled population cleanup in the dedicated fixture, verifies that no zombie-free window remains active, spawns in frame-yielding batches, verifies semantic counts, and then saves. Fixture creation is deliberately separate from measurement.

Run `zombieland/zombie_ticking_run_player_evidence` against each predefined save. It fresh-loads the save before Normal, Fast, Superfast, and Ultrafast; disables RimBridge's private `UltraSpeedBoost` debug default; keeps `forceRequestedSpeed=false`; uses only real-time `rimworld/play_for`; returns the camera after warm-up; records actual time-speed, visible/invisible, priority/remote, type/state, fairness, native-tick completion, update cost, TPS, and FPS; and writes raw JSON to `~/Desktop/ZombieTickingEvidence` by default. The normal reference window is 500 ms warm-up plus 10,000 ms measured time. Do not accept a row as a higher-speed result unless `updateLoop.timeSpeedSamples` contains only the requested speed.

The 2026-07-11 player matrix used `ZL_Ticking_Player_100`, `_500`, `_1000`, and `_2000`; the 1,000- and 2,000-zombie matrices were independently repeated. Across the primary 16 rows, exactly visible and production-priority work had zero skips, selected work equaled actual `CustomTick` work, no zombie was never selected, and the worst fair-queue gap was 122 game ticks. Absolute throughput came from an Apple M4 Max and must not be treated as a portable hardware benchmark.

### Calibrated slow-host evidence

Use `zombieland/zombie_ticking_slow_host_evidence` when Zombieland must be tested inside a game whose other mods, pawns, things, and vanilla systems already consume most main-thread tick time. The companion dynamically patches a `Priority.First` prefix onto `Verse.TickManager.DoSingleTick`, before vanilla and Zombieland tick work but inside Zombieland's complete `TickManagerUpdate` measurement. It charges a calibrated constant CPU cost per real native game tick plus a deterministic periodic spike; it does not sleep, step internal ticks, alter the speed multiplier, or serialize state. A `finally` disables the simulator, unpatches the Harmony owner, and restores the bridge's prior ultrafast debug default.

The tool first binary-searches the constant load against a zero-zombie save at requested Fast, then holds the chosen profile fixed while running zero-zombie and predefined-zombie Normal/Fast/Superfast/Ultrafast matrices. Use `targetFastTicksPerSecond=105`, `spikeMilliseconds=8`, `spikeIntervalTicks=60`, five 2.5-second calibration samples, 500 ms warm-up, and 10-second final samples for the current reference. The final 2026-07-11 profile selected 7.8125 ms per native tick, measured 59.9 TPS at host-only Normal and 106.1 TPS at host-only Fast, and therefore represented a game unable to sustain a practical 2× rate before zombie work. Across the four zombie fixtures, visible, production-priority, and special work remained 100%; no zombie starved; and the worst selection gap was 123 game ticks. Raw evidence is written to `~/Desktop/ZombieTickingEvidence/slow-host/ZL_SlowHost_Evidence.json` by default.

## Zombieland Soundtrack

### Anomaly replacement integration scenario

`anomaly-music-replacement-scenario.lua` is the reusable Steam/RimBridge entry point for the Anomaly sequence integration test. It accepts two controlled loadouts: exactly Harmony, RimBridgeServer, Core, Anomaly, and Zombieland for the smallest focused run, or that tool/mod base plus every official DLC for the repository's current full-DLC scope. Other optional mods remain rejected so their patches cannot contaminate this regression. The scenario starts a fresh built-in debug colony, leaves Zombieland music enabled at 100%, and saves the clean post-test fixture as `ZL_Anomaly_Music_100.rws` by default.

The companion contract provokes RimWorld's real Anomaly predicates independently: a spawned `Noctolith` for `HorrorRelax`, active `UnnaturalDarkness` for `HorrorTension`, and two hostile awake `Metalhorror` pawns for natural `DangerWatcher.High` plus `HorrorCombat`. For each predicate it uses a deterministic seed to compare the track selected at 0% with the exact track handed to Unity at 100%; the latter must be that source track's numbered Zombieland replacement. A scenario-scoped Harmony observer runs before Zombieland's `MusicManagerPlay.PlaySong` prefix and after the original method, recording the sequence's requested source song, the final replacement argument, `CurrentSong`, and the Unity audio clip. The mapping contract also proves that disabling Zombieland music at a retained 50% share neither replaces the song nor consumes shared `Verse.Rand` state.

The tool finalizes `success` only after the observer patch, temporary condition, building, entities, danger state, and requested final music settings have all been verified. The Lua wrapper therefore cannot save a fixture whose cleanup or final-settings postconditions failed.

Always compile the file against the live bridge before executing it:

```text
rimbridge/compile_lua_file
  scriptPath=/Users/ap/Projects/Zombieland/scripts/anomaly-music-replacement-scenario.lua
```

Then run it with no parameters for the default save and seed, or pass `saveName` and `seed` explicitly. The returned result includes the exact active package set, minimal/full-DLC loadout checks, predicate fixtures, disabled RNG-neutrality assertions, 0% control songs, 100% replacement songs, active Unity audio clips, cleanup and final-settings gates, saved fixture, and warning-or-higher journal entries.

Use `sync-soundtrack.sh` to convert and mirror original soundtrack WAV files into the runtime music folder consumed by the dynamic song loader.

Source files live under:

```text
Originals/Soundtrack/
```

Generated runtime files live under:

```text
1.6/Sounds/music/
```

The folder structure is preserved. For example:

```text
Originals/Soundtrack/tense/night/track01.wav
1.6/Sounds/music/tense/night/track01.ogg
```

Use `relax` for normal non-tense map music. It is a naming convention, not a special flag; songs are non-tense unless their path contains `tense`, `danger`, or `combat`.

Files directly under `Originals/Soundtrack/anomaly` are dedicated replacements for Anomaly's ten scripted music tracks, not general shuffle-pool songs. Their names must start with `01` through `10`; the number maps to the corresponding entry in Anomaly's `Songs_Sequences.xml`. At each Anomaly sequence track start, the normal **Play Zombieland music** toggle and Zombieland music-share percentage decide whether the numbered replacement or the original track plays. The replacements retain the original `SongDef` and `MusicSequenceDef` playback roles, so the general mixed-music-mode setting does not change their relax, tension, or combat sequence placement.

Run a normal sync after adding, changing, moving, or deleting tracks:

```bash
./scripts/sync-soundtrack.sh
```

Preview changes without writing files:

```bash
./scripts/sync-soundtrack.sh --dry-run
```

The script converts missing or outdated OGGs, deletes stale generated OGGs whose source WAV no longer exists, and removes empty generated subfolders. Non-OGG files in `1.6/Sounds/music`, such as README files, are left alone. It requires `ffmpeg` on `PATH`, or you can pass an explicit executable:

```bash
./scripts/sync-soundtrack.sh --ffmpeg /path/to/ffmpeg
```

## Zombieland Asset Bundles

Use `build-assetbundles.sh` to compile the Unity project in `Originals/Effects` and deploy the generated bundle into the mod resource folders. This is the supported path for shader and asset bundle work; avoid hand-written Unity command lines unless you are changing this script.

The mod consumes these files:

```text
1.7/Resources/Win64/zombieland
1.7/Resources/Linux/zombieland
1.7/Resources/MacOS/zombieland
```

Unity may also create intermediates under:

```text
Originals/Effects/Assets/AssetBundles/
Originals/Effects/Assets/_Zombieland/
Originals/Effects/Library/
Originals/Effects/UserSettings/
```

Those are not the deployed bundle locations. Treat them as generated build/cache state unless you are deliberately changing the Unity project itself.

### Commands

Full rebuild, for release-like validation or when cross-platform bundles may have changed:

```bash
./scripts/build-assetbundles.sh
./scripts/build-assetbundles.sh --full
```

Quick local iteration, for shader/material changes where only the current machine needs an updated bundle:

```bash
./scripts/build-assetbundles.sh --current
./scripts/build-assetbundles.sh --quick
```

On macOS, `--current` and `--quick` rebuild only:

```text
1.7/Resources/MacOS/zombieland
```

Single explicit target rebuild:

```bash
./scripts/build-assetbundles.sh --os MacOS
./scripts/build-assetbundles.sh --os Linux
./scripts/build-assetbundles.sh --os Win64
```

Accepted aliases include `mac`, `osx`, `darwin`, `linux64`, `win`, `windows`, and `windows64`.

Use a non-default Unity editor executable:

```bash
./scripts/build-assetbundles.sh --unity /path/to/Unity.app/Contents/MacOS/Unity
UNITY_EDITOR=/path/to/Unity.app/Contents/MacOS/Unity ./scripts/build-assetbundles.sh --current
```

The default editor path is:

```text
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity
```

### What The Script Does

The script runs Unity batch mode against `Originals/Effects`, sets `ZOMBIELAND_RESOURCES_DIR` to the repo's `1.7/Resources` directory, and calls one of these Unity static methods:

```text
CreateAssetBundles.BuildStandaloneAssetBundles
CreateAssetBundles.BuildCurrentMachineAssetBundle
CreateAssetBundles.BuildWin64AssetBundle
CreateAssetBundles.BuildLinuxAssetBundle
CreateAssetBundles.BuildMacOSAssetBundle
```

`BuildStandaloneAssetBundles` builds all three platforms. The other methods build one platform only. All methods generate the source assets, run Unity's asset bundle build, copy the produced `zombieland` bundle to `1.7/Resources/{OS}/zombieland`, then validate that the deployed bundle loads the expected assets:

```text
assets/_zombieland/dust.prefab
assets/_zombieland/metaballs.shader
assets/_zombieland/smoke_n.png
assets/_zombieland/smoke_thin.mat
assets/_zombieland/smoke_thin.png
assets/_zombieland/mainmenubackgroundeffect.shader
assets/_zombieland/titleblooddrip.shader
assets/_zombieland/zombiesymbiant.mat
assets/_zombieland/zombiesymbiant.shader
```

The script rejects shader compiler errors, then checks the Unity log for a matching validation line for every requested OS, verifies that the title blood-drip shader contains its update and display passes, prints the SHA-256 of each deployed bundle, and confirms that Unity exited batch mode successfully. Because the supported workflow runs Unity with `-nographics`, this proves shader import, compiler diagnostics, and bundle structure, but it does not claim support on a player's graphics device. Use the live `zombieland/startup_support_state` RimBridge probe to verify the shader, render-texture format, and graphics device in a real game process.

### Expected Output

A successful quick macOS build prints lines similar to:

```text
Building Zombieland asset bundle(s): MacOS
Zombieland bundle validated MacOS: Dust=Dust, Metaballs=Custom/Metaballs, MainMenuBackgroundEffect=Custom/ZombielandMainMenuBackgroundEffect, TitleBloodDrip=Custom/ZombielandTitleBloodDrip, TitleBloodDripPasses=2, ZombieSymbiant=Custom/ZombieSymbiant, assets=9, Unity=2022.3.62f3, path=/Users/ap/Projects/ZombieLand/1.7/Resources/MacOS/zombieland
<sha256>  /Users/ap/Projects/ZombieLand/1.7/Resources/MacOS/zombieland
Exiting batchmode successfully now!
```

The exact SHA-256 changes whenever Unity output changes. The important checks are the correct `1.7/Resources/{OS}/zombieland` path, `assets=9`, and `Exiting batchmode successfully now!`.

### Iteration Speed

Do not clean Unity's generated `Originals/Effects/Library` cache between repeated quick shader iterations unless you need a clean-state check. On this Mac, a quick `--current` build after cache cleanup took about 35 seconds, while the next `--current` build with the cache still warm took about 6 seconds.

The slow cold-cache work was shader variant compilation for Unity's `Particles/Standard Surface` shader. The warm-cache run reported local shader cache hits and compiled 0 variants. For tight edit/build/test loops, run `--current` repeatedly and clean generated Unity files only when parking the work or preparing the tree for review.

### Apple Silicon And Rosetta

Unity `2022.3.62f3` has an arm64 editor binary on Apple Silicon, but its bundled `UnityPackageManager` helper is x86_64. On Apple Silicon, the script verifies Rosetta before starting Unity:

```bash
arch -x86_64 /usr/bin/true
```

If Rosetta is missing, install it once:

```bash
softwareupdate --install-rosetta --agree-to-license
```

Without Rosetta, Unity can fail early with `bad CPU type in executable` or crash while checking the Package Manager helper process.

### Lockfiles And Cleanup

The script handles a stale `Originals/Effects/Temp/UnityLockfile` only when no process owns it. If a running Unity process still holds the lock, the script refuses to continue.

After a build, Unity updates its ignored cache under `Originals/Effects/Library` and creates ignored intermediates under `Originals/Effects/Assets/AssetBundles` and `Originals/Effects/Assets/_Zombieland`. During tight quick-build iteration, keep the cache warm. If a clean-state check or disk cleanup is needed after final verification, preview the ignored files first and then remove only these generated paths:

```bash
git clean -ndX -- Originals/Effects/Library Originals/Effects/Assets/AssetBundles Originals/Effects/Assets/_Zombieland Originals/Effects/UserSettings Originals/Effects/ProjectSettings/MemorySettings.asset Originals/Effects/ProjectSettings/VersionControlSettings.asset
git clean -fdX -- Originals/Effects/Library Originals/Effects/Assets/AssetBundles Originals/Effects/Assets/_Zombieland Originals/Effects/UserSettings Originals/Effects/ProjectSettings/MemorySettings.asset Originals/Effects/ProjectSettings/VersionControlSettings.asset
```

Do not remove or restore the deployed files under `1.7/Resources/{OS}/zombieland` unless you intentionally want to discard the rebuilt bundles.

### Choosing A Mode

Use `--current` or `--quick` while iterating on shader source or generated materials. It is faster and only updates the current machine's bundle.

Use `--os <OS>` when testing one non-current target or when verifying a specific platform regression.

Use `--full` before claiming the asset bundle set is ready for cross-platform use or before preparing a release-like commit.

Do not manually copy files from Unity's intermediate `Assets/AssetBundles` output into the mod. The Unity export method already deploys the correct files to `1.7/Resources/{OS}/zombieland`, and the script verifies those deployed paths.


## New-game and drafted Double Tap controls

The existing `zombieland/settings_state` companion action `new-game` checks
native page-chain reuse and default settings. `wizard-open` opens the native
scenario selection page from Entry for ordinary wizard interaction; it refuses
an existing page chain. Neither action completes world generation by itself.
The existing `zombieland/double_tap_infected_corpse` control now obtains the
native work-giver option with Hunting unassigned, checks undrafted rejection
and drafted acceptance, then invokes that option and observes the real job.
It modifies the loaded fixture; reload the unchanged save afterwards.

## Multiplayer session harness

`python3 -I scripts/mp-session.py MODE` drives a host and a client of the isolated Multiplayer test copy in `artifacts/mp17/Game`. It launches both processes directly over GABP, with the profiles `artifacts/mp17-host-profile` and `artifacts/mp17-client-profile`. It needs a paired deploy into `artifacts/mp17/Game/Mods` first. While it runs, it holds `artifacts/mp17/GAME_IN_USE`. It stops both processes at the end and on failure.

- `fixture --variant base|features|natural` builds a single-player save:
  - `base` (`ZL_MP_Fixture`): a walled colony, armed colonists and every special zombie type.
  - `features` (`ZL_MP_Features`): special zombies only, plus a fueled Thumper, a chainsaw colonist, miners next to granite, zombie corpses, a stockpile, contamination, a Symbiant and a permanent Death Pall (`zombieland/mp_fixture_features`).
  - `natural` (`ZL_MP_Natural`): no zombies, saved at a threat level of at least 50% so that population spawning runs.
- `run --save NAME --ticks N --speed Normal|Fast|Superfast|Ultrafast` loads the save, hosts, joins the client, and advances time only through `multiplayer/set_time_speed`. At a paused boundary it compares clocks, the `desynced` flags, a zombie `(id, cell, state)` hash and the 12 sections of `zombieland/mp_sync_state` (map things, pawn health, colonist flags, switches, Thumpers, chainsaws, areas, storage ranges, settings, advanced constants, contamination, game conditions). It prints `mp-session PASS` or `FAIL` with a compact summary and exits nonzero on failure. Options:
  - `--incident 4|25|100|200` triggers a synchronized Zombieland incident after joining.
  - `--ui` sends Zombieland UI input from both peers through the real UI code (`rimworld/execute_gizmo`, `zombieland/mp_ui_action`) and checks each effect on both peers.
  - `--rejoin-after N` makes the client leave and rejoin after N ticks, which creates a new join point.
  - `--client-constant NAME=VALUE` gives the client a different local advanced setting for the session and checks that the client's settings file stays unchanged.
  - `--async-time` hosts with Multiplayer's per-map clocks.
- `--desync-traces` enables Multiplayer's host traces. Set `desyncTracingMode` to `Slow` in both profiles' `Config/Mod_Multiplayer17_Multiplayer.xml` for stack traces. Slow traces hash stack frames, and JIT inlining differs between processes, so their "first desynced trace" can be a false positive.
- `hold both|host|client --minutes M` keeps peers up for manual steps. `call host|client TOOL 'JSON'` calls one bridge tool on the newest held session.

Full output goes to `artifacts/logs/mp/<stamp>-<mode>/`. Multiplayer writes desync reports to `<profile>/MpDesyncs/Desync-NN.zip`. Never change gameplay state on one peer. `rimworld/execute_debug_action` runs locally and is not synchronized by Multiplayer.
