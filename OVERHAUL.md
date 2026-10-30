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

## What's in it

- **Spore trip (psychedelic effect)** — mild when Mycari are close, intense inside the mushroom grove. Picture: hue drift,
  saturation, breathing lens distortion, chromatic fringes, rainbow bloom, a liquid warp pass with ripples from each mushroom
  note, slow camera roll. Sound: pitch "wow", chorus/flanger, reverb and low-pass on the whole mix + a shimmer pad. Mushroom
  note colours stay readable while a song plays. Lingering patiently next to a Mycari *attunes* you to it: its role colour
  (blue Fixer / red Breaker / green Wanderer) is revealed without Daffodil's scanner.
- **Music & sound from Alexander's tracks** — 80 cues (see `Tools/SoundLibrary/SOUND_LIBRARY.md` for every source + time
  range). Adaptive score: exploration theme that slowly evolves, grove playlist, storm, obelisk, bunker/lore, report song,
  tension bass as life support falls, near-silence while the mushrooms sing. A sound for every interaction, Mycari answer
  in role phrases, dialogue blips per speaker. The puzzle's stock buzzer / winner jingle are replaced at runtime by in-key
  sounds cut from his material.
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
(`AudioDirector` bus sliders), and the balance numbers (`GameBalance` fields).
