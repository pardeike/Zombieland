Place Zombieland soundtrack files in this folder.

This folder is generated from `Originals/Soundtrack` by:

```bash
./scripts/sync-soundtrack.sh
```

Supported file types:
- `.ogg`

The mod creates `SongDef`s for these files at runtime. Do not add one XML def per
track unless the dynamic loader is intentionally replaced.

Folder hints:
- `music/tense/...` marks songs as tense/danger music.
- `music/relax/...` is the recommended place for normal non-tense map music.
- `music/day/...` restricts songs to daytime.
- `music/night/...` restricts songs to nighttime.

Hints can be combined, for example `music/tense/night/track01.ogg`.
