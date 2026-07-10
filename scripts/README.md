# Scripts

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
