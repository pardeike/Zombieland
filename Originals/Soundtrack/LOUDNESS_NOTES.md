# Soundtrack Loudness Notes

Last updated: 2026-07-09

This folder was loudness-matched so the soundtrack files sit at roughly the
same perceived volume when played together. The intent was conservative:
attenuate files that are too loud, avoid boosting quieter files, and preserve
the original WAV shape as 48 kHz stereo 16-bit PCM.

## Target

The main reference target is `-14.47 LUFS` integrated loudness, measured with
EBU R128 via `ffmpeg`'s `loudnorm` filter.

That target came from the quietest original file in the first batch:

- `Zombieland - Mossbyte Overrun.wav`: `-14.47 LUFS`

The first pass adjusted louder tracks down to that level. `Mossbyte Overrun`
was left untouched.

## Current Exception

`Zombieland - Fogfront Outpost.wav` measured correctly after the first pass, but
subjectively felt louder than the rest. It was trimmed by an additional
`-0.70 dB` by ear. Later replacements should preserve the same lower intended
level unless the subjective decision changes.

Current intended value:

- `Zombieland - Fogfront Outpost.wav`: about `-15.17 LUFS`

This is deliberate. Do not automatically bring it back to `-14.47 LUFS` unless
the subjective decision changes.

## Applied Gain Changes

First batch:

| File | Gain Applied |
| --- | ---: |
| `Zombieland - Breach.wav` | `-0.14 dB` |
| `Zombieland - Colony Dawn.wav` | `-0.93 dB` |
| `Zombieland - Dead Horizon.wav` | `-0.51 dB` |
| `Zombieland - Dust Protocol.wav` | `-0.75 dB` |
| `Zombieland - Fogfront Outpost.wav` | `-1.44 dB`, then extra `-0.70 dB` |
| `Zombieland - Outbreak Ritual.wav` | `-0.56 dB` |
| `Zombieland - Mossbyte Overrun.wav` | `0.00 dB` |
| `Zombieland - Rotting Choices.wav` | `-0.85 dB` |
| `Zombieland - Subspace.wav` | `-2.26 dB` |
| `Zombieland - Undead.wav` | `-1.55 dB` |

Later addition:

| File | Before | Gain Applied | After |
| --- | ---: | ---: | ---: |
| `Zombieland - Ambient.wav` | `-13.93 LUFS` | `-0.54 dB` | `-14.47 LUFS` |
| `Zombieland - Danger.wav` | `-14.03 LUFS` | `-0.44 dB` | `-14.47 LUFS` |
| `Zombieland - Day to Night.wav` | `-14.71 LUFS` | `0.00 dB` | `-14.71 LUFS` |
| `Zombieland - Italian Harvest.wav` | `-13.56 LUFS` | `-0.91 dB` | `-14.47 LUFS` |
| `Zombieland - Quiet Stars.wav` | `-11.74 LUFS` | `-2.72 dB` | `-14.47 LUFS` |

Later replacement:

| File | Before | Gain Applied | After |
| --- | ---: | ---: | ---: |
| `Zombieland - Colony Dawn.wav` | `-12.42 LUFS` | `-2.05 dB` | `-14.47 LUFS` |
| `Zombieland - Fogfront Outpost.wav` | `-12.98 LUFS` | `-2.19 dB` | `-15.17 LUFS` |
| `Zombieland - Undead.wav` | `-13.33 LUFS` | `-1.14 dB` | `-14.47 LUFS` |

Later top-level addition:

| File | Before | Gain Applied | After |
| --- | ---: | ---: | ---: |
| `Zombieland - Abyss.wav` | `-13.60 LUFS` | `-0.87 dB` | `-14.47 LUFS` |
| `Zombieland - Blood Horizon.wav` | `-14.11 LUFS` | `-0.36 dB` | `-14.48 LUFS` |
| `Zombieland - Clouds.wav` | `-14.45 LUFS` | `-0.02 dB` | `-14.46 LUFS` |
| `Zombieland - Little Accidents.wav` | `-13.84 LUFS` | `-0.63 dB` | `-14.47 LUFS` |
| `Zombieland - Machine Ghost.wav` | `-13.45 LUFS` | `-1.02 dB` | `-14.47 LUFS` |
| `Zombieland - Restrained.wav` | `-14.76 LUFS` | `0.00 dB` | `-14.76 LUFS` |

Latest addition:

| File | Before | Gain Applied | After |
| --- | ---: | ---: | ---: |
| `Zombieland - Between.wav` | `-14.15 LUFS` | `-0.32 dB` | `-14.48 LUFS` |
| `Zombieland - Last Oracle Part 1.wav` | `-13.41 LUFS` | `-1.06 dB` | `-14.47 LUFS` |
| `Zombieland - Last Oracle Part 2.wav` | `-13.41 LUFS` | `-1.06 dB` | `-14.48 LUFS` |
| `Zombieland - Pale Parade.wav` | `-13.46 LUFS` | `-1.01 dB` | `-14.47 LUFS` |
| `Zombieland - Peaceful Times.wav` | `-14.05 LUFS` | `-0.42 dB` | `-14.47 LUFS` |
| `Zombieland - The Unquiet.wav` | `-13.90 LUFS` | `-0.57 dB` | `-14.47 LUFS` |

Entry-screen addition:

| File | Before | Gain Applied | After |
| --- | ---: | ---: | ---: |
| `entry-screen.wav` | `-13.97 LUFS` | `-0.50 dB` | `-14.47 LUFS` |

Latest sorted addition:

| File | Before | Gain Applied | After |
| --- | ---: | ---: | ---: |
| `tense/Zombieland - Crimson.wav` | `-14.07 LUFS` | `-0.40 dB` | `-14.47 LUFS` |
| `tense/Zombieland - Death Pal.wav` | `-13.96 LUFS` | `-0.51 dB` | `-14.47 LUFS` |
| `relax/Zombieland - Midnight Machine.wav` | `-12.20 LUFS` | `-2.27 dB` | `-14.47 LUFS` |
| `tense/Zombieland - Misalignment.wav` | `-13.97 LUFS` | `-0.50 dB` | `-14.47 LUFS` |
| `relax/Zombieland - Moon Shadow.wav` | `-13.71 LUFS` | `-0.76 dB` | `-14.47 LUFS` |
| `tense/Zombieland - Reshaped.wav` | `-14.19 LUFS` | `-0.28 dB` | `-14.47 LUFS` |
| `tense/Zombieland - Shades.wav` | `-12.99 LUFS` | `-1.49 dB` | `-14.47 LUFS` |
| `tense/Zombieland - Walking.wav` | `-13.87 LUFS` | `-0.60 dB` | `-14.47 LUFS` |

## Backups

The original-volume backup of the first batch is:

- `Originals/Soundtrack.original-volume-backup-20260707-142634/`

The original added file backup is:

- `Zombieland - Italian Harvest.original-volume-backup-20260707-144732.wav`

No backup was retained for the later `Zombieland - Colony Dawn.wav`,
`Zombieland - Fogfront Outpost.wav`, or `Zombieland - Undead.wav`
replacements, by request.

No backup was retained for the later top-level additions, by request.

No backup was retained for the latest additions, by request. The new WAV files
were already copies.

No backup was retained for `entry-screen.wav`; the source file was already a
copy.

No backup was retained for the latest sorted additions, by request.

The actual filenames on disk use an en dash between `Zombieland` and the title.
The names above use a plain hyphen for easier reading in this note.

## Repeatable Workflow For New Files

Measure integrated loudness:

```sh
ffmpeg -hide_banner -nostats \
  -i "Zombieland - New Track.wav" \
  -af "loudnorm=I=-23:TP=-1.5:LRA=11:print_format=json" \
  -f null -
```

Use the reported `input_i` value. If the file is louder than `-14.47 LUFS`,
attenuate it by:

```text
gain_dB = -14.47 - input_i
```

Example: if a new file measures `-13.56 LUFS`, apply `-0.91 dB`.

Apply gain while preserving the current format:

```sh
ffmpeg -y -hide_banner -loglevel error \
  -i "Zombieland - New Track.wav" \
  -af "volume=-0.91dB" \
  -map_metadata 0 \
  -ar 48000 -ac 2 -c:a pcm_s16le \
  "Zombieland - New Track.adjusted.wav"
```

Then replace the original after checking it.

Verify format:

```sh
ffprobe -v error -select_streams a:0 \
  -show_entries stream=codec_name,sample_rate,channels,duration \
  -of default=noprint_wrappers=1 \
  "Zombieland - New Track.wav"
```

Expected format:

- `codec_name=pcm_s16le`
- `sample_rate=48000`
- `channels=2`

## Practical Rule

Use the meter for the first pass, then trust listening for small exceptions.
Integrated LUFS can match while a track still feels louder because of
arrangement, density, percussion, saturation, or upper-mid frequency content.
For those cases, a small extra trim of about `-0.5 dB` to `-1.0 dB` is usually
the right scale.
