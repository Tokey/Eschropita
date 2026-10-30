# Eschropita overhaul

Everything lives in `Assets/Overhaul/` and installs itself at runtime when the main scene plays (`OverhaulBootstrap` +
`[OverhaulModule]`). **The scene file is not modified**: original visuals that the overhaul covers (the stock windmill,
the stone tile in the mushroom grove, the Mycari home box) are only hidden while it runs.

## Controls

| Key | What it does |
|---|---|
| **E** | Interact (as before) — also reads lore terminals (E / Esc to close, W/S to scroll) |
| **F7** | Spore-trip strength: 100 % → 50 % → off (remembered; scales the hit/miss flashes too) |
| **F9** | Toggle the redesigned level dressing vs. the original visuals (before/after) |
| **F10** | Toggle the new UI type vs. the original type (before/after) |

## What's in it

- **Spore trip (psychedelic effect)** — kept faint so everything stays visible: a slight shimmer near Mycari, a little
  more in the grove. It only peaks on your answers to a mushroom song, and the two look and sound different:
  - **Hit** (right mushroom, solved song): warm and harmonious, with golden bloom, a lift toward the note's colour, a
    gentle outward breath, a ring of light rolling out from the pad, and a reverb/pad swell. A solved song blooms
    longest.
  - **Miss** (wrong mushroom): cold and dissonant, with drained colour, magenta/cyan toning, hard colour fringes, a
    pinch, a violet vignette, slices of the picture shivering sideways, a short camera shake, and the whole mix going
    off-key for a moment.

  Tune it on `TripDirector` (ambient levels, `hitSeconds` / `solvedSeconds` / `missSeconds`). Lingering patiently next
  to a Mycari *attunes* you to it: its role colour (blue Fixer / red Breaker / green Wanderer) is revealed without
  Daffodil's scanner.
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
- **Winning Mycari over** (game code: `Assets/GameFlow/MycariPersuasion.cs`) — in the sandbox, asking a Mycari (E)
  sends it to the mushroom grove. Meet it there and it sings its melody on the mushrooms. Sing it back and it listens:
  it follows you and takes any order. A wrong note and it turns red, wanders home, and won't hear you out for 20 s.
  A Mycari you've won over remembers your song (the tutorial Fixer too): ask again and it follows at once. One song
  at a time. Melodies start at 4 notes (Breakers 5), grow every two Mycari won (up to +2), and drop a note after a
  miss.
- **Balance** (`GameBalance`, runtime only) — with the windmill at a quarter health, life support lasts about two
  minutes: about 70 s to the 25 % alarm, then an emergency reserve halves the drain. That's time for a song, a miss
  and a retry. Recovery is quick once the windmill turns again. Tornadoes do 2.5 health per second.
- **Tornado fixes** (game code: `TornadoCalamity`) — the funnel now follows the ground. Its anchor sat at y = 0,
  about 8 m under the terrain, so random tornadoes were invisible and never hit anything. Random tornadoes now also
  go for the windmill 40 % of the time (more while the obelisk sings): they bear down on it and weave over it for
  about 6 s, taking roughly 30 % of its health. They leave a windmill below 35 % alone.
- **Mycari pathing** — the JoyTech dome stood on top of the Mycari's roam area, and its walls cut their NavMesh,
  trapping them. Structures and props now keep clear of the roam area and the walks between landmarks. The dome
  moved behind the home, outside it. Idle Mycari also stay out of the mushroom grove (`NPCManager.groveKeepOut`), so
  they no longer stand on the singing mushrooms; the widened roam box reached into it.
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
