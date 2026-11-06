#!/usr/bin/env bash
# Makes tone.mp3, the test track of the end-to-end runs: four seconds of a 440 Hz tone, mono, 32 kbit/s,
# with ID3 tags (artist and title) so Webamp shows them. Run once with ffmpeg; the result is committed.
set -euo pipefail
cd "$(dirname "$0")"
ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=440:duration=4" -ac 1 -ar 22050 -b:a 32k \
  -metadata artist="Kazo Test" -metadata title="Tone 440" -id3v2_version 3 tone.mp3
