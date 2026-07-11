# Scripts

## XML Validation

Run `./scripts/check-versioned-xml.sh` after changing defs, patches, language data, `LoadFolders.xml`, or mod metadata. The check parses every tracked or non-ignored project XML file, verifies the versioned runtime layout, rejects unexpected direct text in active container documents, detects empty or duplicate active translation entries, detects duplicate active def names, requires active non-English keyed files to have the same key set as English, requires every active language to have the same DefInjected key set, verifies every DefInjected key targets a real English def and full field path (including RimWorld's stable named-list and quest-node paths), preserves runtime placeholders and formatting tokens, requires every direct English def label and description to be translated in every active non-English language, and rejects duplicate effective labels among active non-abstract ThingDefs after each language's injections are applied.

RimWorld's live translation report also lists inherited defaults that are not present in Zombieland's English XML. Do not add language-only keys for those false positives. The intentional exclusions are the inherited generic death messages for `ToxicSplatter` and `SeismicWave`, `Zombies.leaderTitle`, `ZombieBite.labelNounPretty`, and the default `Mote` labels on Zombieland's internal visual-only ThingDefs. Translation entries must continue to correspond to explicit English source fields.

The preserved `1.4/` payload is still checked for XML parse failures and layout isolation, but its known legacy translation structure is frozen and is not subjected to the newer active-language anomaly and parity rules.

## RimWorld Log Summary

Run `./scripts/summarize-rimworld-log.sh /path/to/Player.log` to collapse repeated error blocks before inspecting a runtime test. The summary includes exceptions plus early play-data failures such as `Config error in`, grammar-rule failures, and failed resolution messages; those can be emitted before RimBridge initializes and therefore must not be checked only through the live bridge journal.

## Zombie Ticking Runtime Benchmark

`zombie-ticking-benchmark.lua` is the reusable real-RimBridge entry point for the adaptive scheduler regression and speed matrix. The lowered-Lua file is intentionally a thin parameter wrapper around `zombieland/zombie_ticking_benchmark`; the companion tool owns the sequence so its C# `finally` restores test mode even when a nested call fails or the operation is cancelled.

Always compile the file against the live bridge before executing it:

```text
rimbridge/compile_lua_file
  scriptPath=/Users/ap/Projects/Zombieland/scripts/zombie-ticking-benchmark.lua
```

For the disposable 25-zombie matrix, run it against `EMPTY` with `spawnCount=25`, the spawn centered at `(10,10)`, the camera at `(240,240)`, `durationMs=2500`, `forceRequestedSpeed=false`, and `runContracts=true`. This runs the adaptive-feedback, fair-scheduler, and path-payment contracts once, then reloads the save for independent Normal, Fast, Superfast, and Ultrafast samples. The companion uses `zombieland/zombie_ticking_test_mode` to suppress zero-threat cleanup from both the ordinary setting and the scheduled zombie-free event during measurement. Its single-run gate and non-cancelled teardown restore the copied settings timeline on success, failure, or cancellation; `zombieland/zombie_ticking_benchmark_cleanup_contract` injects both failure modes through that same scope helper.

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

Use the async `zombieland/zombie_ticking_create_player_fixture` companion tool to create deterministic, reusable 250×250 player saves with production-random zombie types. Build 100, 500, 1,000, and 2,000 variants from the same clean base save, keep the default 0.25% chances for each special type, and give each population a distinct seed. The tool places 10% of the horde in the exact camera rectangle, the remainder outside the 12-cell camera-protection margin, disables population-changing zero-threat events in the dedicated fixture, spawns in frame-yielding batches, verifies semantic counts, and then saves. Fixture creation is deliberately separate from measurement.

Run `zombieland/zombie_ticking_run_player_evidence` against each predefined save. It fresh-loads the save before Normal, Fast, Superfast, and Ultrafast; disables RimBridge's private `UltraSpeedBoost` debug default; keeps `forceRequestedSpeed=false`; uses only real-time `rimworld/play_for`; returns the camera after warm-up; records actual time-speed, visible/invisible, priority/remote, type/state, fairness, native-tick completion, update cost, TPS, and FPS; and writes raw JSON to `~/Desktop/ZombieTickingEvidence` by default. The normal reference window is 500 ms warm-up plus 10,000 ms measured time. Do not accept a row as a higher-speed result unless `updateLoop.timeSpeedSamples` contains only the requested speed.

The 2026-07-11 player matrix used `ZL_Ticking_Player_100`, `_500`, `_1000`, and `_2000`; the 1,000- and 2,000-zombie matrices were independently repeated. Across the primary 16 rows, exactly visible and production-priority work had zero skips, selected work equaled actual `CustomTick` work, no zombie was never selected, and the worst fair-queue gap was 122 game ticks. Absolute throughput came from an Apple M4 Max and must not be treated as a portable hardware benchmark.

### Calibrated slow-host evidence

Use `zombieland/zombie_ticking_slow_host_evidence` when Zombieland must be tested inside a game whose other mods, pawns, things, and vanilla systems already consume most main-thread tick time. The companion dynamically patches a `Priority.First` prefix onto `Verse.TickManager.DoSingleTick`, before vanilla and Zombieland tick work but inside Zombieland's complete `TickManagerUpdate` measurement. It charges a calibrated constant CPU cost per real native game tick plus a deterministic periodic spike; it does not sleep, step internal ticks, alter the speed multiplier, or serialize state. A `finally` disables the simulator, unpatches the Harmony owner, and restores the bridge's prior ultrafast debug default.

The tool first binary-searches the constant load against a zero-zombie save at requested Fast, then holds the chosen profile fixed while running zero-zombie and predefined-zombie Normal/Fast/Superfast/Ultrafast matrices. Use `targetFastTicksPerSecond=105`, `spikeMilliseconds=8`, `spikeIntervalTicks=60`, five 2.5-second calibration samples, 500 ms warm-up, and 10-second final samples for the current reference. The final 2026-07-11 profile selected 7.8125 ms per native tick, measured 59.9 TPS at host-only Normal and 106.1 TPS at host-only Fast, and therefore represented a game unable to sustain a practical 2× rate before zombie work. Across the four zombie fixtures, visible, production-priority, and special work remained 100%; no zombie starved; and the worst selection gap was 123 game ticks. Raw evidence is written to `~/Desktop/ZombieTickingEvidence/slow-host/ZL_SlowHost_Evidence.json` by default.

## Zombieland Soundtrack

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
1.6/Resources/Win64/zombieland
1.6/Resources/Linux/zombieland
1.6/Resources/MacOS/zombieland
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
1.6/Resources/MacOS/zombieland
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

The script runs Unity batch mode against `Originals/Effects`, sets `ZOMBIELAND_RESOURCES_DIR` to the repo's `1.6/Resources` directory, and calls one of these Unity static methods:

```text
CreateAssetBundles.BuildStandaloneAssetBundles
CreateAssetBundles.BuildCurrentMachineAssetBundle
CreateAssetBundles.BuildWin64AssetBundle
CreateAssetBundles.BuildLinuxAssetBundle
CreateAssetBundles.BuildMacOSAssetBundle
```

`BuildStandaloneAssetBundles` builds all three platforms. The other methods build one platform only. All methods generate the source assets, run Unity's asset bundle build, copy the produced `zombieland` bundle to `1.6/Resources/{OS}/zombieland`, then validate that the deployed bundle loads the expected assets:

```text
assets/_zombieland/dust.prefab
assets/_zombieland/metaballs.shader
assets/_zombieland/smoke_n.png
assets/_zombieland/smoke_thin.mat
assets/_zombieland/smoke_thin.png
assets/_zombieland/mainmenubackgroundeffect.shader
assets/_zombieland/zombiesymbiant.mat
assets/_zombieland/zombiesymbiant.shader
```

The script then checks the Unity log for a matching validation line for every requested OS, prints the SHA-256 of each deployed bundle, and confirms that Unity exited batch mode successfully.

### Expected Output

A successful quick macOS build prints lines similar to:

```text
Building Zombieland asset bundle(s): MacOS
Zombieland bundle validated MacOS: Dust=Dust, Metaballs=Custom/Metaballs, MainMenuBackgroundEffect=Custom/ZombielandMainMenuBackgroundEffect, ZombieSymbiant=Custom/ZombieSymbiant, assets=8, Unity=2022.3.62f3, path=/Users/ap/Projects/ZombieLand/1.6/Resources/MacOS/zombieland
<sha256>  /Users/ap/Projects/ZombieLand/1.6/Resources/MacOS/zombieland
Exiting batchmode successfully now!
```

The exact SHA-256 changes whenever Unity output changes. The important checks are the correct `1.6/Resources/{OS}/zombieland` path, `assets=8`, and `Exiting batchmode successfully now!`.

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

Do not remove or restore the deployed files under `1.6/Resources/{OS}/zombieland` unless you intentionally want to discard the rebuilt bundles.

### Choosing A Mode

Use `--current` or `--quick` while iterating on shader source or generated materials. It is faster and only updates the current machine's bundle.

Use `--os <OS>` when testing one non-current target or when verifying a specific platform regression.

Use `--full` before claiming the asset bundle set is ready for cross-platform use or before preparing a release-like commit.

Do not manually copy files from Unity's intermediate `Assets/AssetBundles` output into the mod. The Unity export method already deploys the correct files to `1.6/Resources/{OS}/zombieland`, and the script verifies those deployed paths.
