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
