#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
SOURCE_DIR="$ROOT/Originals/Soundtrack"
DEST_DIR="$ROOT/1.6/Sounds/music"
FFMPEG_BIN="${FFMPEG:-ffmpeg}"
QUALITY=5
FORCE=false
DRY_RUN=false
DELETE_STALE=true

usage() {
	cat <<'EOF'
Usage: ./scripts/sync-soundtrack.sh [options]

Converts Originals/Soundtrack/**/*.wav to 1.6/Sounds/music/**/*.ogg and keeps
the generated OGG tree in sync with the WAV source tree.

Defaults:
  source: ./Originals/Soundtrack
  dest:   ./1.6/Sounds/music

Options:
  --source DIR       Source folder containing WAV files.
  --dest DIR         Output folder for generated OGG files.
  --quality N        Vorbis quality, passed to ffmpeg -q:a. Default: 5.
  --ffmpeg PATH      ffmpeg executable. Default: $FFMPEG or ffmpeg on PATH.
  --force            Reconvert every WAV even when the OGG is newer.
  --no-delete        Do not delete stale OGG files.
  --dry-run          Print planned changes without writing files.
  -h, --help         Show this help.

Folder structure is mirrored. For example:
  Originals/Soundtrack/tense/night/track01.wav
  -> 1.6/Sounds/music/tense/night/track01.ogg
EOF
}

die() {
	printf 'sync-soundtrack: %s\n' "$*" >&2
	exit 1
}

log() {
	printf '%s\n' "$*"
}

abs_dir() {
	local dir="$1"
	mkdir -p "$dir"
	(cd "$dir" && pwd)
}

while (($#)); do
	case "$1" in
		--source)
			[[ $# -ge 2 ]] || die "--source requires a directory"
			SOURCE_DIR="$2"
			shift 2
			;;
		--dest)
			[[ $# -ge 2 ]] || die "--dest requires a directory"
			DEST_DIR="$2"
			shift 2
			;;
		--quality)
			[[ $# -ge 2 ]] || die "--quality requires a value"
			QUALITY="$2"
			shift 2
			;;
		--ffmpeg)
			[[ $# -ge 2 ]] || die "--ffmpeg requires an executable path"
			FFMPEG_BIN="$2"
			shift 2
			;;
		--force)
			FORCE=true
			shift
			;;
		--no-delete)
			DELETE_STALE=false
			shift
			;;
		--dry-run)
			DRY_RUN=true
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		*)
			die "unknown option: $1"
			;;
	esac
done

[[ "$QUALITY" =~ ^-?[0-9]+([.][0-9]+)?$ ]] || die "--quality must be numeric"

SOURCE_DIR="$(abs_dir "$SOURCE_DIR")"
DEST_DIR="$(abs_dir "$DEST_DIR")"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT
EXPECTED_OGGS="$TMP_DIR/expected-oggs.txt"
touch "$EXPECTED_OGGS"

converted=0
skipped=0
deleted=0
planned_conversions=0
planned_deletions=0
ffmpeg_checked=false
encoder_checked=false
vorbis_args=()

ensure_ffmpeg() {
	if [[ "$ffmpeg_checked" == true ]]; then
		return
	fi
	if ! command -v "$FFMPEG_BIN" >/dev/null 2>&1; then
		die "ffmpeg not found. Install ffmpeg or pass --ffmpeg /path/to/ffmpeg"
	fi
	ffmpeg_checked=true
}

detect_vorbis_encoder() {
	if [[ "$encoder_checked" == true ]]; then
		return
	fi

	ensure_ffmpeg
	local encoders
	encoders="$("$FFMPEG_BIN" -hide_banner -encoders 2>/dev/null || true)"
	if printf '%s\n' "$encoders" | awk '$2 == "libvorbis" { found = 1 } END { exit found ? 0 : 1 }'; then
		vorbis_args=(-c:a libvorbis)
	elif printf '%s\n' "$encoders" | awk '$2 == "vorbis" { found = 1 } END { exit found ? 0 : 1 }'; then
		# FFmpeg's native Vorbis encoder is experimental and only accepts stereo.
		vorbis_args=(-ac 2 -c:a vorbis -strict -2)
	else
		die "ffmpeg has no Vorbis encoder. Install an ffmpeg build with libvorbis or native vorbis support."
	fi
	encoder_checked=true
}

needs_convert() {
	local src="$1"
	local dest="$2"
	if [[ "$FORCE" == true ]]; then
		return 0
	fi
	if [[ ! -f "$dest" ]]; then
		return 0
	fi
	if [[ "$src" -nt "$dest" ]]; then
		return 0
	fi
	return 1
}

convert_wav() {
	local src="$1"
	local rel="${src#"$SOURCE_DIR"/}"
	local dest_rel="${rel%.*}.ogg"
	local dest="$DEST_DIR/$dest_rel"
	printf '%s\n' "$dest_rel" >> "$EXPECTED_OGGS"

	if ! needs_convert "$src" "$dest"; then
		((skipped += 1))
		return
	fi

	if [[ "$DRY_RUN" == true ]]; then
		log "would convert: $rel -> $dest_rel"
		((planned_conversions += 1))
		return
	fi

	detect_vorbis_encoder
	mkdir -p "$(dirname "$dest")"
	local tmp
	tmp="$(mktemp "$(dirname "$dest")/.tmp.XXXXXX")"
	if "$FFMPEG_BIN" -hide_banner -loglevel error -y -i "$src" -vn -map_metadata 0 "${vorbis_args[@]}" -q:a "$QUALITY" -f ogg "$tmp"; then
		mv "$tmp" "$dest"
		((converted += 1))
		log "converted: $rel -> $dest_rel"
	else
		rm -f "$tmp"
		die "ffmpeg failed for $rel"
	fi
}

delete_stale_ogg() {
	local ogg="$1"
	local rel="${ogg#"$DEST_DIR"/}"
	if grep -Fxq -- "$rel" "$EXPECTED_OGGS"; then
		return
	fi

	if [[ "$DRY_RUN" == true ]]; then
		log "would delete stale: $rel"
		((planned_deletions += 1))
		return
	fi

	rm -f "$ogg"
	((deleted += 1))
	log "deleted stale: $rel"
}

while IFS= read -r -d '' wav; do
	convert_wav "$wav"
done < <(find "$SOURCE_DIR" -type f -iname '*.wav' -print0)

if [[ "$DELETE_STALE" == true ]]; then
	while IFS= read -r -d '' ogg; do
		delete_stale_ogg "$ogg"
	done < <(find "$DEST_DIR" -type f -iname '*.ogg' -print0)
fi

if [[ "$DRY_RUN" == false ]]; then
	find "$DEST_DIR" -depth -mindepth 1 -type d -empty -exec rmdir {} +
fi

if [[ "$DRY_RUN" == true ]]; then
	log "Dry run complete: $planned_conversions conversion(s), $planned_deletions stale deletion(s), $skipped up-to-date file(s)."
else
	log "Soundtrack sync complete: $converted converted, $deleted stale deleted, $skipped up to date."
fi
