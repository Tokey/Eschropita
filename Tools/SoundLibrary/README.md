# Sound library tools

The game's music, ambience, Mycari phrases, stingers and tonal UI/SFX are cut from the sound designer's (Alexander's)
tracks — the SoundCloud sets *SP SOUND LIBRARY*, *Mushroom 4 Part Loops* and *Mushroom Game Layered*. Only non-tonal
sounds (footsteps, hiss, impacts, machinery, scanner sweeps) are synthesized.

```bash
pip install yt-dlp numpy scipy matplotlib     # and ffmpeg on PATH
./get_tracks.sh ./_tracks                       # download + decode the 25 tracks
python analyze.py ./_tracks/wav analysis.json   # tempo / key / parts / onsets
python build_sound_library.py --src ./_tracks/wav --project ../.. --analysis analysis.json
```

Output: `Assets/Overhaul/Resources/Overhaul/Audio/` (+ `manifest.json`, read at runtime by `AudioCatalog`) and
`SOUND_LIBRARY.md` here, which lists every cue with its exact source track and time range.
The SoundCloud streams are 160 kbps AAC; if the original lossless files are available, decode them to the same names
(e.g. `Mushroom_4_Part_Loop_017.wav`) and re-run — every selection stays the same.
`notes.py` is the onset search used to choose the three source notes (bell / pluck / soft) the tonal UI sounds are pitched from.
