#!/usr/bin/env bash
# Makes the test tracks of the end-to-end runs: four seconds of a tone, mono, 32 kbit/s, with ID3 tags
# (artist and title) so Webamp shows them: tone.mp3 (440 Hz, "Tone 440") and tone-220.mp3 (220 Hz,
# "Tone 220", a second track told apart by its title). Run once with ffmpeg; the results are committed.
set -euo pipefail
cd "$(dirname "$0")"
for frequency in 440 220; do
  name=tone.mp3
  [ "$frequency" = 440 ] || name="tone-$frequency.mp3"
  ffmpeg -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=$frequency:duration=4" -ac 1 -ar 22050 -b:a 32k \
    -metadata artist="Kazo Test" -metadata title="Tone $frequency" -id3v2_version 3 "$name"
done
