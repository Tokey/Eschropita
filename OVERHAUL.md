# Eschropita overhaul

Everything lives in `Assets/Overhaul/` and installs itself at runtime when the main scene plays (`OverhaulBootstrap` +
`[OverhaulModule]`). **The scene file is not modified**: original visuals that the overhaul covers (the stock windmill,
the stone tile in the mushroom grove, the Mycari home box) are only hidden while it runs.

## Controls

| Key | What it does |
|---|---|
| **E** | Interact (as before) — also reads lore terminals (E / Esc to close, W/S to scroll) |
| **F7** | Spore-trip strength: 100 % → 50 % → off (remembered) |
| **F9** | Toggle the redesigned level dressing vs. the original visuals (before/after) |
| **F10** | Toggle the new UI type vs. the original type (before/after) |

## What's in it

- **Spore trip (psychedelic effect)** — mild when Mycari are close, intense inside the mushroom grove. Picture: hue drift,
  saturation, breathing lens distortion, chromatic fringes, rainbow bloom, a liquid warp pass with ripples from each mushroom
  note, slow camera roll. Sound: pitch "wow", chorus/flanger, reverb and low-pass on the whole mix + a shimmer pad. Mushroom
  note colours stay readable while a song plays. **Focus:** while the mushrooms sing and you answer them, the spores
  settle (about 40 % calmer, a bit more when you stand still and listen) so the puzzle is easy to follow; solving the song
  lets the full trip bloom back. Tune it on `TripDirector` → *Focus while doing the mushroom puzzle* (`puzzleCalm`,
  `stillnessCalm`, `settleSeconds`, `bloomBackSeconds`). Lingering patiently next to a Mycari *attunes* you to it: its role colour
  (blue Fixer / red Breaker / green Wanderer) is revealed without Daffodil's scanner.
- **Music & sound from Alexander's tracks** — 80 cues (see `Tools/SoundLibrary/SOUND_LIBRARY.md` for every source + time
  range). Adaptive score: exploration theme that slowly evolves, grove playlist, storm, obelisk, bunker/lore, report song,
  tension bass as life support falls, near-silence while the mushrooms sing. A sound for every interaction, Mycari answer
  in role phrases, dialogue blips per speaker. The puzzle's stock buzzer / winner jingle are replaced at runtime by in-key
  sounds cut from his material.
- **Footsteps** — one step per foot contact of Escropita's run cycle (they keep time with her legs, storm slow-downs
  included), a soft gravel crunch on regolith and a spongy squish on the grove's mycelium floor, alternating feet, never
  the same take twice in a row.
- **UI type** (`UiType`) — a refined 70s soft serif for titles and grades (Fraunces, capitals evenly tracked) and
  speaker names (Fraunces Italic), and a Futura-style 60s/70s sans for everything you read (Jost, one size step up so it
  reads larger than before). Colours, panels and their transparency are exactly the game's own; the only effect is a
  faint soft shadow under the letters. Fonts are SIL OFL (`Assets/Overhaul/Resources/Overhaul/Fonts`, licences alongside).
- **Intro** — the intro cut scene is *SP_ INTRO CUT SCENE_001* (1080p, 2:06, skippable as before), same file name and GUID
  as the old `Assets/Videos/intro.mp4`, so the scene reference is unchanged.
- **Atmosphere** — moods per act (day / grove / storm / obelisk / bunker / hope), no more white flash at start, red
  breathing overlay when life support is critical.
- **Tactile feedback** — particles, light flashes, ground rings, beams, a glowing tether to your follower, repair streams,
  camera impulses, hit-stop on big beats, squash-and-stretch Mycari.
- **Mechanics** (design doc) — Field Assessment (repair / dismantle / freedom ×4), six lore terminals (Aniya, Vera and
  Danu verbatim; Hargrave, the surveillance dossier and the JoyTech brochure are marked DRAFT in
  `Assets/Overhaul/Resources/Overhaul/Lore/`), graded report card from Reishi, Chaga and Maitake (Excellent / Moderate /
  Poor endings), animated life-support gauge, objective banner, light paths revealed by the first song.
- **Balance** (`GameBalance`, runtime only) — gentler life-support drain with an emergency reserve below 25 %, faster
  recovery, adaptive melody length (4 → 6, eases off after two misses), Mycari you've sung with accept any command,
  softer tornado damage.
- **Level** — Mars wind turbine (spins with windmill health, fungal repair patches, shard-shield plates), visible
  life-support plant with conduits to the dome door, organic glowing grove floor, JoyTech biodome + airlock door,
  Escropita's lander, ruins, surveillance masts, bunker hatch, horizon landmarks. Textures: CC0 Poly Haven
  (`Tools/LevelTextures/CREDITS.md`).

## Small hooks added to existing scripts

`PlayerController` (lore interactables on E), `MartianSequencePuzzle` (pad signals), `GameHUD` (toast signal, hide the old
life bar while the gauge shows), `GameFlowManager` (bunker-song and report-card hooks), `NPCFlocker` (acceptance hook),
`DialogueSystem` (`IsTyping`). Launch with `-noOverhaul` to run the game without any of it.

## Please check in Unity

This was written without a Unity editor. Every script was compile-checked against Unity 6000.5.0f1 / HDRP 17.5 /
uGUI 2.5 / Cinemachine 3.1 source (0 errors, editor and player builds), but nothing has been seen rendered or heard
in-engine yet. Worth a look on first play: the look of the procedural turbine/dome/grove, the strength of the trip warp
(`TripVisuals`, `Hidden/Eschropita/TripWarp` — it switches itself off if the shader fails), mix levels
(`AudioDirector` bus sliders), the balance numbers (`GameBalance` fields), and the UI type (`UiType`; the HUD's
text sizes were checked against GameHUD's layout on paper only).
