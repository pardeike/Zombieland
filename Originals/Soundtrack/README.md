Place original Zombieland soundtrack WAV files in this folder.

Run this after adding, moving, changing, or deleting tracks:

```bash
./scripts/sync-soundtrack.sh
```

The script mirrors this folder into `1.6/Sounds/music` as OGG files. For example:

```text
Originals/Soundtrack/tense/night/track01.wav
1.6/Sounds/music/tense/night/track01.ogg
```

Folder hints are preserved for the runtime dynamic `SongDef` loader:

- `tense` marks songs as tense/danger music.
- `relax` is the recommended place for normal non-tense map music.
- `day` restricts songs to daytime.
- `night` restricts songs to nighttime.

Hints can be combined, for example `tense/night/track01.wav`.

## Anomaly Replacements

WAV files directly under `anomaly` are 1:1 replacements for Anomaly's ten scripted music tracks. Start each filename with `01` through `10` to map it to the corresponding Anomaly song in source order. These tracks are excluded from the general Zombieland shuffle; whenever Anomaly starts the mapped song, the existing Zombieland music toggle and share percentage determine whether the replacement or the original plays.

Do not add Anomaly's later ambience, attack, or end-credit audio to this numbered replacement set. The dedicated replacements inherit the original song settings and their existing relax, tension, and combat sequence behavior.
