#!/bin/bash
# Downloads the sound designer's SoundCloud sets and decodes them to 44.1 kHz WAVs named by title,
# ready for build_sound_library.py.   usage: ./get_tracks.sh <work dir>
# Needs: python3 with yt-dlp (pip install yt-dlp numpy scipy), ffmpeg on PATH.
set -e
WORK="${1:-./_tracks}"; mkdir -p "$WORK/raw" "$WORK/wav"
cd "$WORK/raw"
for u in "https://soundcloud.com/alexander-857450270/sets/sp-sound-library" \
         "https://soundcloud.com/alexander-857450270/sets/mushroom-4-part-loops/s-CLZUwbH3hxw" \
         "https://soundcloud.com/alexander-857450270/mushroom-game-layer/s-V7xmbQuBrav"; do
  python3 -m yt_dlp --no-warnings -f "hls_aac_160k/bestaudio" --download-archive archive.txt --write-info-json \
    -o "%(id)s - %(title)s.%(ext)s" "$u"
done
for f in *.m4a; do
  t=$(python3 -c "import json,re,sys; print(re.sub(r'[^A-Za-z0-9]+','_',json.load(open(sys.argv[1][:-4]+'.info.json'))['title']).strip('_'))" "$f")
  [ -f "../wav/$t.wav" ] || ffmpeg -v error -y -i "$f" -ac 2 -ar 44100 -c:a pcm_s16le "../wav/$t.wav"
done
echo "WAVs in $WORK/wav"
