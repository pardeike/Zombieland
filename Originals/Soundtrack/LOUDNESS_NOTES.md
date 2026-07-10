# Soundtrack Loudness Policy

Use this file for durable normalization rules, not a chronological list of files processed. Git history preserves earlier per-batch measurements and gain changes.

## Current Target

- Measure integrated loudness with EBU R128 through `ffmpeg`'s `loudnorm` filter.
- Target approximately `-14.47 LUFS` integrated.
- Only attenuate tracks louder than the target. Do not boost quieter tracks unless explicitly requested.
- Preserve source WAVs as 48 kHz, stereo, 16-bit PCM (`pcm_s16le`).
- Preserve duration unless a separate edit explicitly requires otherwise.

The target came from `Zombieland – Mossbyte Overrun.wav`, the quietest original in the first normalized batch. It remains the reference; the fact that earlier source files were normalized in place does not require a permanent per-file gain ledger.

## Deliberate Exception

`Zombieland – Fogfront Outpost.wav` meters correctly at the common target but subjectively sounds louder because of its arrangement. Its intended level is approximately `-15.17 LUFS`, about `0.70 dB` below the main target.

Do not automatically normalize this track back to `-14.47 LUFS`. Similar small listening-based trims of roughly `-0.5 dB` to `-1.0 dB` are reasonable when a meter-matched track still jumps out.

## Repeatable Workflow

Measure integrated loudness:

```sh
ffmpeg -hide_banner -nostats \
  -i "Zombieland – New Track.wav" \
  -af "loudnorm=I=-23:TP=-1.5:LRA=11:print_format=json" \
  -f null -
```

Use the reported `input_i` value. For a track louder than the target, calculate:

```text
gain_dB = -14.47 - input_i
```

For example, a track measuring `-13.56 LUFS` needs `-0.91 dB` attenuation. A track measuring `-14.71 LUFS` is already quieter than the target and should not be boosted.

Apply the gain to a temporary file while preserving the source format:

```sh
ffmpeg -y -hide_banner -loglevel error \
  -i "Zombieland – New Track.wav" \
  -af "volume=-0.91dB" \
  -map_metadata 0 \
  -ar 48000 -ac 2 -c:a pcm_s16le \
  "Zombieland – New Track.adjusted.wav"
```

Listen to the adjusted file before replacing the source. Follow the backup rule in `AGENTS.md` unless the user explicitly waives it.

Verify the final format:

```sh
ffprobe -v error -select_streams a:0 \
  -show_entries stream=codec_name,sample_rate,channels,duration \
  -of default=noprint_wrappers=1 \
  "Zombieland – New Track.wav"
```

Expected values:

- `codec_name=pcm_s16le`
- `sample_rate=48000`
- `channels=2`

After source changes, run `./scripts/sync-soundtrack.sh` from the repository root to regenerate the runtime OGG mirror.

## What Belongs Here

Update this file only when the target, a deliberate per-track exception, the required source format, or the repeatable normalization procedure changes. Do not append batch tables, backup inventories, operation logs, or one-time gain calculations.
