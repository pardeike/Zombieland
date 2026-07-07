# Soundtrack Agent Notes

Before adjusting files in this folder, read `LOUDNESS_NOTES.md`.

For new WAV soundtrack files:

- Preserve format: `pcm_s16le`, `48000 Hz`, stereo.
- Preserve duration unless explicitly asked otherwise.
- Measure integrated loudness with EBU R128 via `ffmpeg loudnorm`.
- Main target is `-14.47 LUFS`.
- Only attenuate files that are louder than the target; do not boost quieter
  files unless explicitly asked.
- `Zombieland - Fogfront Outpost.wav` is intentionally lower, about
  `-15.17 LUFS`, because it subjectively felt louder after meter matching.
- Back up originals before overwriting.
- Small subjective trims of about `-0.5 dB` to `-1.0 dB` are acceptable when
  the meter says a file matches but listening says it still jumps out.

The actual soundtrack filenames use an en dash between `Zombieland` and the
title. Plain hyphens may be used in notes and examples for easier typing.
