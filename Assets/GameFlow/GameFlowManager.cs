using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// Drives the scripted story beats and hands over to the sandbox in between.
///
/// Intro video → scan tutorial → a Mycari arrives and shows the melody → the player repeats it
/// → the Mycari repairs the windmill → a tornado appears (and leaves) → a tornado hits the windmill
/// → SANDBOX (keep the windmill running, life support, recruit/command Mycari)
/// → obelisk shield → shards → windmill shield → obelisk core → bunker → final reports → outro video.
///
/// Every scene reference is auto-resolved when left empty, and every optional system is null-safe,
/// so the flow still runs (with beats skipped) in a scene that is missing pieces.
/// Life support reaching zero returns the player to the last checkpoint and restarts the flow at
/// that checkpoint's phase; phases are written to be state-aware so re-entering one is safe.
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("Scene references (auto-resolved when empty)")]
    [SerializeField] PlayerController player;
    [SerializeField] FollowPlayer daffodil;
    [SerializeField] TaskSite windmill;
    [SerializeField] MartianSequencePuzzle puzzle;
    [SerializeField] MushroomFieldTrigger mushroomField;
    [SerializeField] TornadoCalamity tornado;
    [SerializeField] NPCManager npcManager;
    [SerializeField] Obelisk obelisk;
    [SerializeField] BunkerTrigger bunker;
    [SerializeField] Transform newLocationViewpoint;
    [SerializeField] IsoFollowCamera cameraRig;
    [SerializeField] GameHUD hud;
    [SerializeField] DialogueSystem dialogue;
    [SerializeField] CutsceneVideoPlayer video;
    [SerializeField] LifeSupport lifeSupport;
    [SerializeField] CheckpointManager checkpoints;

    [Header("Media (leave empty for placeholders)")]
    [Tooltip("Played before anything else. Skippable. A placeholder card is shown when empty.")]
    [SerializeField] VideoClip introClip;
    [Tooltip("Played after the final report. Skippable. A placeholder card is shown when empty.")]
    [SerializeField] VideoClip outroClip;
    [Tooltip("The 'new music' the Mycari play inside the bunker. A small procedural melody plays when empty.")]
    [SerializeField] AudioClip bunkerMusic;

    [Header("Debug")]
    [Tooltip("Start the flow from this phase (world state is prepared to match). IntroVideo = normal play.")]
    [SerializeField] GamePhase debugStartPhase = GamePhase.IntroVideo;

    [Header("Tuning")]
    [Tooltip("Seconds the player must hold V before the tutorial moves on.")]
    [SerializeField] float scanTutorialSeconds = 1.0f;
    [Tooltip("Sandbox time before Daffodil points out the obelisk (unless the player scans it first).")]
    [SerializeField] float sandboxSecondsBeforeObelisk = 75f;
    [SerializeField] float tornadoIntroHoldSeconds = 8f;
    [SerializeField] float tornadoStrikeHoldSeconds = 12f;
    [Tooltip("Windmill health (0-1) after the scripted strike, if the tornado didn't do enough on its own.")]
    [Range(0f, 1f)] [SerializeField] float windmillHealthAfterStrike = 0.25f;
    [Tooltip("Tornado intensity while the obelisk core is exposed and humming.")]
    [SerializeField] float obeliskCoreTornadoIntensity = 1.6f;
    [Tooltip("Tornado intensity after the obelisk is destroyed.")]
    [SerializeField] float calmTornadoIntensity = 0.75f;
    [Tooltip("How long the camera lingers on the new location after the bunker.")]
    [SerializeField] float revealSeconds = 4f;
    [Tooltip("Life support is topped up to at least this (0-1) when returning to a checkpoint.")]
    [Range(0f, 1f)] [SerializeField] float lifeSupportFloorOnRestore = 0.6f;

    [Header("Dialogue (edit freely)")]
    [SerializeField] DialogueLine[] scanTutorialLines =
    {
        L("Daffodil", "Systems up. Barely. Welcome to Mars, Eschropita."),
        L("Daffodil", "I'm your scanner, your map, and apparently your babysitter. Hold V and I'll show you what's out there."),
    };
    [SerializeField] DialogueLine[] scanDoneLines =
    {
        L("Daffodil", "See? Everything that matters lights up. Everything else is dust."),
        L("Daffodil", "…Something's coming. Small. Fungal. Don't panic."),
    };
    [SerializeField] DialogueLine[] mycariArrivedLines =
    {
        L("Daffodil", "A Mycari. Local. They don't talk, they sing. It wants you to follow it."),
    };
    [SerializeField] DialogueLine[] demoStartLines =
    {
        L("Daffodil", "Watch the mushrooms. It's showing you a melody."),
    };
    [SerializeField] DialogueLine[] puzzleStartLines =
    {
        L("Daffodil", "Your turn. Walk up to a mushroom and press E. Same order. Try not to embarrass us."),
    };
    [Tooltip("Shown as a toast (not a dialogue) so the replay isn't hidden. Rotates through the list.")]
    [SerializeField] string[] puzzleFailToasts =
    {
        "Daffodil: Wrong. Again.",
        "Daffodil: That was… creative. Again.",
        "Daffodil: Watch the mushrooms. Then press the mushrooms. In that order.",
    };
    [SerializeField] DialogueLine[] puzzleSuccessLines =
    {
        L("Daffodil", "Fine. You can hum. Congratulations, you're bilingual in mushroom."),
        L("Daffodil", "Look, it's heading for the windmill. I think you just got yourself a mechanic."),
    };
    [SerializeField] DialogueLine[] windmillRepairedLines =
    {
        L("Daffodil", "Windmill's back up. That thing powers our life support, so keep it that way."),
    };
    [SerializeField] DialogueLine[] tornadoIntroLines =
    {
        L("Daffodil", "Pressure drop. Storm cell forming. Dust tornado."),
        L("Daffodil", "Stay out of its path. It'll shred you and anything you care about."),
    };
    [SerializeField] DialogueLine[] tornadoGoneLines =
    {
        L("Daffodil", "It's gone. For now. They come back."),
    };
    [SerializeField] DialogueLine[] tornadoStrikeLines =
    {
        L("Daffodil", "It's turning. It's heading for the windmill—"),
    };
    [SerializeField] DialogueLine[] windmillStruckLines =
    {
        L("Daffodil", "Power's down. Life support runs on that windmill."),
        L("Daffodil", "Find a Fixer, the green ones, and get it running. Breakers, the red ones, only know how to take things apart."),
        L("Daffodil", "Scan with V to tell them apart. Scanning drains me, so don't hold it for fun."),
    };
    [SerializeField] DialogueLine[] lifeSupportLowLines =
    {
        L("Daffodil", "Life support at a quarter. Just saying."),
    };
    [SerializeField] DialogueLine[] gameOverLines =
    {
        L("Daffodil", "…and that's zero. Rolling back to the last checkpoint. Do better."),
    };
    [SerializeField] DialogueLine[] obeliskFoundLines =
    {
        L("Daffodil", "New reading. A structure, shielded, old. Older than the colony. The Mycari avoid it."),
        L("Daffodil", "The shield is energy. Breakers could take it down. Bring a few."),
    };
    [SerializeField] DialogueLine[] obeliskShieldBrokenLines =
    {
        L("OBELISK", "…WE WERE THE WIND. WE WERE THE WIND BEFORE YOU NAMED IT."),
        L("Daffodil", "Well. That's ominous."),
        L("Daffodil", "Those shards hold a charge. Pick them up. We can build a shield around the windmill with them. Press E at the windmill."),
    };
    [SerializeField] DialogueLine[] obeliskCoreLines =
    {
        L("Daffodil", "Shield's holding. But the storms are getting worse, and that thing is… singing. Louder every minute."),
        L("Daffodil", "I think it's the source. Bring Breakers. Take it all the way down."),
    };
    [SerializeField] DialogueLine[] obeliskDestroyedLines =
    {
        L("Daffodil", "It's quiet. The wind's… normal. There's a hatch under the rubble."),
    };
    [SerializeField] DialogueLine[] bunkerLines =
    {
        L("TERMINAL", "SURVEY OUTPOST 7. LOG 1: The wind sings at night. The obelisk answers."),
        L("TERMINAL", "LOG 2: We built the windmills to drown it out. It grew louder."),
        L("TERMINAL", "LOG 3: The Mycari know something. We should have listened."),
        L("Daffodil", "…Listen."),
    };
    [SerializeField] DialogueLine[] newLocationLines =
    {
        L("Daffodil", "The Mycari are playing something new. Over there. Look."),
    };
    [SerializeField] DialogueLine[] finalReportLines =
    {
        L("FIXER", "♪ The windmill turns. The dome breathes. We patched what you broke and what the wind broke. ♪"),
        L("BREAKER", "♪ The stone that sang is silent. Its shards keep your wind. We are not sorry. ♪"),
        L("WANDERER", "♪ We walked far. The valley past the ridge is green. Follow, when you're ready. ♪"),
        L("Daffodil", "That's the report. Green valley, silent stone, running windmill. Not bad for a first week, Eschropita."),
    };

    // ---------------- public state ----------------

    public GamePhase Phase { get; private set; } = GamePhase.IntroVideo;
    public static event Action<GamePhase> OnPhaseChanged;

    /// <summary>False during videos, camera reveals, the final report and game over.</summary>
    public bool PlayerControlEnabled { get; private set; } = true;

    /// <summary>True once the melody has been learned: recruited Mycari accept commands.</summary>
    public bool MycariObey { get; private set; }

    public bool SandboxUnlocked => Phase >= GamePhase.Sandbox;
    public string CurrentObjective { get; private set; } = "";

    // ---------------- flags set by events / notifications ----------------

    bool _mushroomEntered;
    bool _bunkerEntered;
    bool _obeliskScanned;
    bool _puzzleSucceeded;
    int _puzzleFails;
    int _tornadoSpawns;
    int _tornadoEnds;
    bool _shieldBroken;
    bool _obeliskCracked;
    bool _obeliskDestroyed;
    bool _handlingGameOver;
    bool _lowWarned;

    Coroutine _flow;
    NPCFlocker _tutorialMycari;
    AudioSource _music;
    float _sandboxStartTime;
    int _failToastIndex;

    static DialogueLine L(string speaker, string text) => new DialogueLine { speaker = speaker, text = text };

    // ---------------- Unity ----------------

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        ResolveReferences();
        Subscribe();
        _flow = StartCoroutine(RunFlow(debugStartPhase));
    }

    void OnDestroy()
    {
        Unsubscribe();
        if (Instance == this) Instance = null;
    }

    // ---------------- notifications (called by triggers / scans) ----------------

    public void NotifyMushroomFieldEntered() => _mushroomEntered = true;
    public void NotifyBunkerEntered() => _bunkerEntered = true;
    public void NotifyObeliskScanned() => _obeliskScanned = true;

    // ---------------- wiring ----------------

    void ResolveReferences()
    {
        if (!player) player = FindAnyObjectByType<PlayerController>();
        if (!daffodil) daffodil = FollowPlayer.Instance ? FollowPlayer.Instance : FindAnyObjectByType<FollowPlayer>();
        if (!puzzle) puzzle = FindAnyObjectByType<MartianSequencePuzzle>();
        if (!mushroomField) mushroomField = FindAnyObjectByType<MushroomFieldTrigger>();
        if (!tornado) tornado = FindAnyObjectByType<TornadoCalamity>();
        if (!npcManager) npcManager = NPCManager.Instance ? NPCManager.Instance : FindAnyObjectByType<NPCManager>();
        if (!cameraRig) cameraRig = FindAnyObjectByType<IsoFollowCamera>();

        if (!windmill)
        {
            windmill = TaskManager.Instance ? TaskManager.Instance.FindByType(TaskType.PowerPlant) : null;
            if (!windmill)
                foreach (var s in FindObjectsByType<TaskSite>(FindObjectsSortMode.None))
                    if (s.type == TaskType.PowerPlant) { windmill = s; break; }
        }

        // Systems that can live on this object: create them if the scene has none.
        if (!hud) hud = GameHUD.Instance ? GameHUD.Instance : FindAnyObjectByType<GameHUD>();
        if (!hud) hud = new GameObject("HUD").AddComponent<GameHUD>();

        if (!dialogue) dialogue = DialogueSystem.Instance ? DialogueSystem.Instance : FindAnyObjectByType<DialogueSystem>();
        if (!dialogue) dialogue = gameObject.AddComponent<DialogueSystem>();

        if (!video) video = CutsceneVideoPlayer.Instance ? CutsceneVideoPlayer.Instance : FindAnyObjectByType<CutsceneVideoPlayer>();
        if (!video) video = gameObject.AddComponent<CutsceneVideoPlayer>();

        if (!lifeSupport) lifeSupport = LifeSupport.Instance ? LifeSupport.Instance : FindAnyObjectByType<LifeSupport>();
        if (!lifeSupport) lifeSupport = gameObject.AddComponent<LifeSupport>();

        if (!checkpoints) checkpoints = CheckpointManager.Instance ? CheckpointManager.Instance : FindAnyObjectByType<CheckpointManager>();
        if (!checkpoints) checkpoints = gameObject.AddComponent<CheckpointManager>();

        if (!obelisk) obelisk = FindAnyObjectByType<Obelisk>();
        if (!obelisk) obelisk = CreateDummyObelisk();

        if (obelisk)
        {
            if (!bunker) bunker = obelisk.GetComponentInChildren<BunkerTrigger>(true);
            if (!newLocationViewpoint)
            {
                var vp = obelisk.transform.Find("NewLocationViewpoint");
                if (!vp)
                {
                    // Twelve metres further away from the windmill, a little above ground.
                    Vector3 away = windmill ? (obelisk.transform.position - windmill.transform.position).normalized : Vector3.back;
                    if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                    vp = new GameObject("NewLocationViewpoint").transform;
                    vp.SetParent(obelisk.transform, false);
                    vp.position = obelisk.transform.position + away * 12f + Vector3.up * 2f;
                }
                newLocationViewpoint = vp;
            }
        }
        if (!bunker) bunker = FindAnyObjectByType<BunkerTrigger>(FindObjectsInactive.Include);

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 0.55f;
    }

    /// <summary>
    /// Builds the dummy obelisk when the scene has none, so the whole flow is playable without
    /// running the editor setup tool. Placed on the far side of the windmill from the Mycari home.
    /// </summary>
    Obelisk CreateDummyObelisk()
    {
        Vector3 basePos = windmill ? windmill.transform.position + new Vector3(-2f, 0f, -26f)
                                   : (player ? player.transform.position + new Vector3(-10f, 0f, -18f) : new Vector3(-14f, 9f, -13f));
        if (NavMesh.SamplePosition(basePos, out var hit, 30f, NavMesh.AllAreas)) basePos = hit.position;
        else if (Physics.Raycast(basePos + Vector3.up * 50f, Vector3.down, out var rh, 200f)) basePos = rh.point;

        var go = new GameObject("Obelisk");
        go.transform.position = basePos;
        var site = go.AddComponent<TaskSite>();
        site.type = TaskType.Obelisk;
        site.displayName = "Obelisk";
        site.allowRepair = false;
        site.allowDismantle = true;
        site.tornadoCanDamage = false;
        site.workRadius = 4.5f;
        site.workDuration = 0f;
        var col = go.AddComponent<CapsuleCollider>();
        col.radius = 1.2f; col.height = 7f; col.center = new Vector3(0f, 3.5f, 0f);
        go.AddComponent<AudioSource>();
        var ob = go.AddComponent<Obelisk>();
        Debug.Log("[GameFlow] No Obelisk in scene: created a dummy one at " + basePos + ". Run Tools ▸ Eschropita ▸ Set Up Game Flow In Scene to persist it.");
        return ob;
    }

    void Subscribe()
    {
        if (puzzle)
        {
            puzzle.OnSuccess += OnPuzzleSuccess;
            puzzle.OnFail += OnPuzzleFail;
        }
        if (tornado)
        {
            tornado.OnSpawned += OnTornadoSpawned;
            tornado.OnEnded += OnTornadoEnded;
        }
        if (obelisk)
        {
            obelisk.OnShieldBroken += OnObeliskShieldBroken;
            obelisk.OnCracked += OnObeliskCracked;
            obelisk.OnDestroyed += OnObeliskDestroyed;
        }
        if (lifeSupport)
        {
            lifeSupport.OnDepleted += OnLifeSupportDepleted;
            lifeSupport.OnLow += OnLifeSupportLow;
        }
        if (windmill)
        {
            windmill.OnDepleted += OnWindmillDepleted;
            windmill.OnFullyRepaired += OnWindmillRepaired;
        }
        if (checkpoints) checkpoints.OnSaved += OnCheckpointSaved;
    }

    void Unsubscribe()
    {
        if (puzzle)
        {
            puzzle.OnSuccess -= OnPuzzleSuccess;
            puzzle.OnFail -= OnPuzzleFail;
        }
        if (tornado)
        {
            tornado.OnSpawned -= OnTornadoSpawned;
            tornado.OnEnded -= OnTornadoEnded;
        }
        if (obelisk)
        {
            obelisk.OnShieldBroken -= OnObeliskShieldBroken;
            obelisk.OnCracked -= OnObeliskCracked;
            obelisk.OnDestroyed -= OnObeliskDestroyed;
        }
        if (lifeSupport)
        {
            lifeSupport.OnDepleted -= OnLifeSupportDepleted;
            lifeSupport.OnLow -= OnLifeSupportLow;
        }
        if (windmill)
        {
            windmill.OnDepleted -= OnWindmillDepleted;
            windmill.OnFullyRepaired -= OnWindmillRepaired;
        }
        if (checkpoints) checkpoints.OnSaved -= OnCheckpointSaved;
    }

    // ---------------- event handlers ----------------

    void OnPuzzleSuccess() => _puzzleSucceeded = true;
    void OnPuzzleFail() => _puzzleFails++;
    void OnTornadoSpawned()
    {
        _tornadoSpawns++;
        if (SandboxUnlocked) Toast("Tornado forming");
    }
    void OnTornadoEnded() => _tornadoEnds++;
    void OnObeliskShieldBroken() => _shieldBroken = true;
    void OnObeliskCracked() => _obeliskCracked = true;
    void OnObeliskDestroyed() => _obeliskDestroyed = true;
    void OnCheckpointSaved() => Toast("Checkpoint");

    void OnWindmillDepleted(TaskSite s)
    {
        if (SandboxUnlocked) Toast("Windmill down. Life support is draining");
    }

    void OnWindmillRepaired(TaskSite s)
    {
        if (SandboxUnlocked) Toast("Windmill at full power");
    }

    void OnLifeSupportLow()
    {
        if (!SandboxUnlocked || _handlingGameOver) return;
        if (_lowWarned) { Toast("Life support low"); return; }
        _lowWarned = true;
        if (dialogue && !dialogue.IsPlaying()) dialogue.Play(lifeSupportLowLines);
        else Toast("Life support low");
    }

    void OnLifeSupportDepleted()
    {
        if (_handlingGameOver || !SandboxUnlocked) return;
        StartCoroutine(GameOverRoutine());
    }

    // ---------------- the flow ----------------

    IEnumerator RunFlow(GamePhase start)
    {
        // Let every other Awake/Start (singletons, HUD build, NavMesh agents) settle first.
        yield return null;

        PrepareForPhase(start);
        GamePhase next = start;

        while (true)
        {
            SetPhase(next);
            switch (Phase)
            {
                case GamePhase.IntroVideo:    yield return PhaseIntroVideo();    next = GamePhase.ScanTutorial;  break;
                case GamePhase.ScanTutorial:  yield return PhaseScanTutorial();  next = GamePhase.MycariArrives; break;
                case GamePhase.MycariArrives: yield return PhaseMycariArrives(); next = GamePhase.MycariDemo;    break;
                case GamePhase.MycariDemo:    yield return PhaseMycariDemo();    next = GamePhase.PlayerPuzzle;  break;
                case GamePhase.PlayerPuzzle:  yield return PhasePlayerPuzzle();  next = GamePhase.MycariRepairs; break;
                case GamePhase.MycariRepairs: yield return PhaseMycariRepairs(); next = GamePhase.TornadoIntro;  break;
                case GamePhase.TornadoIntro:  yield return PhaseTornadoIntro();  next = GamePhase.TornadoStrike; break;
                case GamePhase.TornadoStrike: yield return PhaseTornadoStrike(); next = GamePhase.Sandbox;       break;
                case GamePhase.Sandbox:       yield return PhaseSandbox();       next = GamePhase.ObeliskShield; break;
                case GamePhase.ObeliskShield: yield return PhaseObeliskShield(); next = GamePhase.ObeliskCore;   break;
                case GamePhase.ObeliskCore:   yield return PhaseObeliskCore();   next = GamePhase.Bunker;        break;
                case GamePhase.Bunker:        yield return PhaseBunker();        next = GamePhase.FinalReport;   break;
                case GamePhase.FinalReport:   yield return PhaseFinalReport();   next = GamePhase.OutroVideo;    break;
                case GamePhase.OutroVideo:    yield return PhaseOutroVideo();    next = GamePhase.End;           break;
                case GamePhase.End:           yield return PhaseEnd();           yield break;
            }
        }
    }

    void SetPhase(GamePhase p)
    {
        Phase = p;
        OnPhaseChanged?.Invoke(p);
    }

    /// <summary>
    /// Puts the world in the state a phase expects. Used for debug starts and for restarting
    /// at a checkpoint, so it must be idempotent.
    /// </summary>
    void PrepareForPhase(GamePhase p)
    {
        SetControl(true);

        if (p >= GamePhase.MycariRepairs) MycariObey = true;

        if (p >= GamePhase.Sandbox)
        {
            EnsureSandboxSystems();
            // A debug jump straight into the sandbox needs something to fix.
            if (p == GamePhase.Sandbox && windmill && debugStartPhase == GamePhase.Sandbox && !checkpoints.HasCheckpoint)
                windmill.SetState(windmill.maxHealth * windmillHealthAfterStrike, 0f);
        }
        else if (tornado)
        {
            tornado.SetSuppressed(true);
        }

        if (p >= GamePhase.ObeliskShield && obelisk)
        {
            obelisk.Discovered = true;
            if (hud) hud.ObeliskVisible = true;
        }

        if (p >= GamePhase.ObeliskCore && obelisk)
        {
            if (obelisk.Stage == ObeliskStage.Shielded) obelisk.RestoreStage(ObeliskStage.Exposed);
            obelisk.StartHum();
            if (tornado) tornado.SetIntensity(obeliskCoreTornadoIntensity);
        }

        if (p >= GamePhase.Bunker && obelisk)
        {
            if (obelisk.Stage != ObeliskStage.Destroyed) obelisk.RestoreStage(ObeliskStage.Destroyed);
            if (tornado) tornado.SetIntensity(calmTornadoIntensity);
        }

        if (p >= GamePhase.FinalReport) _bunkerEntered = true;
    }

    void EnsureSandboxSystems()
    {
        MycariObey = true;
        if (npcManager && npcManager.All.Count <= 1)
            npcManager.SpawnByRoles(npcManager.workerCount, npcManager.attackerCount, npcManager.wandererCount);
        if (lifeSupport) lifeSupport.active = true;
        if (tornado)
        {
            tornado.SetSuppressed(false);
            if (Phase < GamePhase.ObeliskCore) tornado.SetIntensity(1f);
        }
    }

    // ---- phases ----

    IEnumerator PhaseIntroVideo()
    {
        SetControl(false);
        yield return PlayVideo(introClip, "ESCHROPITA");
        SetControl(true);
    }

    IEnumerator PhaseScanTutorial()
    {
        Objective("Hold V to scan with Daffodil");
        if (daffodil) daffodil.Refill();
        yield return Say(scanTutorialLines);

        if (daffodil)
        {
            float start = daffodil.TotalScanSeconds;
            yield return new WaitUntil(() => daffodil.TotalScanSeconds - start >= scanTutorialSeconds);
        }
        else yield return new WaitForSeconds(1f);

        yield return Say(scanDoneLines);
    }

    IEnumerator PhaseMycariArrives()
    {
        if (!npcManager || !player)
        {
            Debug.LogWarning("[GameFlow] No NPCManager/Player: skipping the Mycari arrival beat.");
            yield break;
        }

        // Spawn one Fixer at the Mycari home and walk it up to the player.
        Vector3 home = npcManager.GetRandomNavPointInside();
        _tutorialMycari = npcManager.SpawnAt(NPCRole.Worker, home);
        if (!_tutorialMycari) yield break;

        Objective("Something is coming…");
        bool arrived = false;
        Vector3 toPlayer = (player.transform.position - home).normalized;
        _tutorialMycari.ScriptedMoveTo(player.transform.position - toPlayer * 2.5f, 3f, () => arrived = true);
        yield return new WaitUntil(() => arrived || !_tutorialMycari);
        if (!_tutorialMycari) yield break;

        yield return Say(mycariArrivedLines);

        if (!mushroomField)
        {
            Debug.LogWarning("[GameFlow] No MushroomFieldTrigger: skipping the walk to the field.");
            yield break;
        }

        Objective("Follow the Mycari to the mushroom field");
        _mushroomEntered = false;
        bool atField = false;
        _tutorialMycari.ScriptedMoveTo(mushroomField.Center, 2.5f, () => atField = true);

        // The Mycari reaching the grove is what starts the melody — leading it there is the task.
        // `atField` is the fallback for a field whose trigger volume is too small (or missing) for
        // the walk to register, so a mis-sized collider cannot dead-end the opening.
        yield return new WaitUntil(() =>
            mushroomField.MycariInside || atField || !_tutorialMycari);
    }

    IEnumerator PhaseMycariDemo()
    {
        if (!puzzle) yield break;
        puzzle.SetSequenceLength(4, 4); // "plays something easy"
        Objective("Watch the Mycari's melody");
        yield return Say(demoStartLines);

        if (_tutorialMycari)
        {
            bool done = false;
            puzzle.StartDemo(_tutorialMycari, () => done = true);
            yield return new WaitUntil(() => done);
        }
    }

    IEnumerator PhasePlayerPuzzle()
    {
        if (!puzzle) yield break;
        _puzzleSucceeded = false;
        _puzzleFails = 0;
        int seenFails = 0;

        yield return Say(puzzleStartLines);
        Objective("Repeat the melody: walk up to a mushroom and press E");
        puzzle.BeginPlayerAttempt();

        while (!_puzzleSucceeded)
        {
            if (_puzzleFails > seenFails)
            {
                seenFails = _puzzleFails;
                if (puzzleFailToasts != null && puzzleFailToasts.Length > 0)
                    Toast(puzzleFailToasts[_failToastIndex++ % puzzleFailToasts.Length], 2.2f);
            }
            yield return null;
        }

        yield return Say(puzzleSuccessLines);
    }

    IEnumerator PhaseMycariRepairs()
    {
        MycariObey = true;
        if (!windmill) yield break;

        // Make sure there is visibly something to fix.
        if (windmill.Health01 > 0.6f) windmill.SetState(windmill.maxHealth * 0.45f, windmill.shield);

        Objective("Watch the Mycari repair the windmill");
        if (_tutorialMycari)
        {
            _tutorialMycari.ScriptedAssign(windmill, TaskCommand.Repair);
            float deadline = Time.time + 40f;
            yield return new WaitUntil(() => windmill.Health01 >= 0.999f || Time.time >= deadline || !_tutorialMycari);
            if (windmill.Health01 < 0.999f) windmill.SetState(windmill.maxHealth, windmill.shield);

            // Job done: it walks back to the others rather than dissolving into the roam state
            // on the spot. Seeing it leave is what sells that you borrowed it, not kept it.
            if (_tutorialMycari) yield return SendHome(_tutorialMycari);
        }
        else
        {
            windmill.SetState(windmill.maxHealth, windmill.shield);
            yield return new WaitForSeconds(1f);
        }

        yield return Say(windmillRepairedLines);
    }

    IEnumerator PhaseTornadoIntro()
    {
        if (!tornado) yield break;

        Objective("Stay clear of the tornado");
        Vector3 target = StrikePointNearPlayer(14f);
        int spawns = _tornadoSpawns, ends = _tornadoEnds;
        tornado.SetSuppressed(false);
        tornado.StrikeAt(target, tornadoIntroHoldSeconds);

        yield return new WaitUntil(() => _tornadoSpawns > spawns);
        yield return Say(tornadoIntroLines);
        yield return new WaitUntil(() => _tornadoEnds > ends);
        tornado.SetSuppressed(true);

        yield return Say(tornadoGoneLines);
    }

    IEnumerator PhaseTornadoStrike()
    {
        if (!tornado || !windmill) yield break;

        int spawns = _tornadoSpawns, ends = _tornadoEnds;
        tornado.SetSuppressed(false);
        tornado.StrikeAt(windmill.transform.position, tornadoStrikeHoldSeconds);

        yield return new WaitUntil(() => _tornadoSpawns > spawns);
        yield return Say(tornadoStrikeLines);
        Objective("…");
        yield return new WaitUntil(() => _tornadoEnds > ends);

        // The beat must land even if the tornado's wander never quite reached the collider.
        if (windmill.Health01 > windmillHealthAfterStrike)
            windmill.SetState(windmill.maxHealth * windmillHealthAfterStrike, 0f);

        yield return Say(windmillStruckLines);
    }

    IEnumerator PhaseSandbox()
    {
        EnsureSandboxSystems();
        _lowWarned = false;
        if (daffodil) daffodil.Refill();
        if (lifeSupport && lifeSupport.Value01 < lifeSupportFloorOnRestore) lifeSupport.Set(lifeSupport.max * lifeSupportFloorOnRestore);
        if (checkpoints) checkpoints.Save("Sandbox");

        Objective("Keep the windmill running: scan (V), recruit a Fixer (E), order a repair");
        _sandboxStartTime = Time.time;
        _obeliskScanned = false;

        // Move on once the player has settled in (windmill healthy for a while) or has found the obelisk.
        yield return new WaitUntil(() =>
            _obeliskScanned ||
            (Time.time - _sandboxStartTime >= sandboxSecondsBeforeObelisk && (!windmill || windmill.Health01 >= 0.6f)));

        if (!obelisk) yield break;
        yield return Say(obeliskFoundLines);
    }

    IEnumerator PhaseObeliskShield()
    {
        if (!obelisk) yield break;
        obelisk.Discovered = true;
        if (hud) hud.ObeliskVisible = true;

        if (obelisk.Stage == ObeliskStage.Shielded)
        {
            Objective("Bring Breakers to the Obelisk and break its shield");
            _shieldBroken = false;
            yield return new WaitUntil(() => _shieldBroken || obelisk.Stage != ObeliskStage.Shielded);
            yield return Say(obeliskShieldBrokenLines);
            if (checkpoints) checkpoints.Save("Obelisk exposed");
        }

        if (windmill && windmill.canBuildShield && windmill.shield <= 0f)
        {
            Objective("Collect the shards, then build a shield at the windmill (E)");
            // Fallback so the story can't dead-end if every shard is lost: wait for a shield,
            // or for the shards to be gone with none in the player's pocket for a while.
            float noShardsSince = -1f;
            yield return new WaitUntil(() =>
            {
                if (windmill.shield > 0f) return true;
                bool none = ObeliskShard.ActiveCount == 0 && (!player || player.Shards < windmill.shardsPerShield);
                if (!none) { noShardsSince = -1f; return false; }
                if (noShardsSince < 0f) noShardsSince = Time.time;
                return Time.time - noShardsSince > 20f;
            });
            if (windmill.shield <= 0f)
            {
                Toast("Daffodil: The shards are gone. I'll route the charge myself.");
                windmill.AddShield(windmill.shieldPerBuild);
            }
        }

        obelisk.StartHum();
        if (tornado) tornado.SetIntensity(obeliskCoreTornadoIntensity);
        yield return Say(obeliskCoreLines);
    }

    IEnumerator PhaseObeliskCore()
    {
        if (!obelisk) yield break;
        Objective("Destroy the Obelisk core");
        _obeliskDestroyed = obelisk.Stage == ObeliskStage.Destroyed;
        yield return new WaitUntil(() => _obeliskDestroyed || obelisk.Stage == ObeliskStage.Destroyed);

        if (tornado) tornado.SetIntensity(calmTornadoIntensity);
        yield return Say(obeliskDestroyedLines);
        if (checkpoints) checkpoints.Save("Obelisk destroyed");
    }

    IEnumerator PhaseBunker()
    {
        Objective("Enter the bunker beneath the Obelisk");
        if (bunker && !bunker.gameObject.activeInHierarchy) bunker.gameObject.SetActive(true);
        _bunkerEntered = _bunkerEntered || (bunker && bunker.Entered);
        yield return new WaitUntil(() => _bunkerEntered);

        yield return Say(bunkerLines);

        // "Mycari plays new music" — a clip if assigned, otherwise a small procedural melody.
        PlayBunkerMusic();

        // "New location is shown" — linger on the viewpoint, then hand the camera back.
        if (cameraRig && newLocationViewpoint && player)
        {
            SetControl(false);
            cameraRig.SetTarget(newLocationViewpoint);
            yield return Say(newLocationLines);
            yield return new WaitForSeconds(revealSeconds);
            cameraRig.SetTarget(player.transform);
            SetControl(true);
        }
        else yield return Say(newLocationLines);

        if (checkpoints) checkpoints.Save("Bunker");
    }

    IEnumerator PhaseFinalReport()
    {
        Objective("Listen to the reports");
        SetControl(false);

        var reporters = new List<NPCFlocker>();
        if (npcManager && player)
        {
            Vector3 p = player.transform.position;
            foreach (NPCRole role in new[] { NPCRole.Worker, NPCRole.Attacker, NPCRole.Wanderer })
            {
                var n = npcManager.FindNearest(role, p, x => !x.IsScripted && !reporters.Contains(x));
                if (!n) n = npcManager.SpawnAt(role, p + UnityEngine.Random.insideUnitSphere.normalized * 8f);
                if (n) reporters.Add(n);
            }

            int arrived = 0;
            for (int i = 0; i < reporters.Count; i++)
            {
                float ang = (i / (float)reporters.Count) * Mathf.PI * 2f;
                Vector3 spot = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 3f;
                reporters[i].ScriptedMoveTo(spot, 1.5f, () => arrived++);
            }
            float deadline = Time.time + 20f;
            yield return new WaitUntil(() => arrived >= reporters.Count || Time.time >= deadline);
        }

        yield return Say(finalReportLines);

        foreach (var n in reporters) if (n) n.ScriptedRelease();
    }

    IEnumerator PhaseOutroVideo()
    {
        SetControl(false);
        if (_music && _music.isPlaying) _music.Stop();
        yield return PlayVideo(outroClip, "ESCHROPITA — THE END");
    }

    IEnumerator PhaseEnd()
    {
        Objective("");
        SetControl(false);
        bool again = false;
        if (hud) hud.ShowEnd("Green valley. Silent stone. Running windmill.", () => again = true);
        else again = true;
        yield return new WaitUntil(() => again);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------------- game over / checkpoint ----------------

    IEnumerator GameOverRoutine()
    {
        _handlingGameOver = true;
        if (_flow != null) StopCoroutine(_flow);
        _flow = null;
        SetControl(false);
        if (tornado) tornado.ForceStop();

        yield return Say(gameOverLines);

        bool confirmed = false;
        if (hud) hud.ShowGameOver("The windmill stopped and the air ran out.", () => confirmed = true);
        else confirmed = true;
        yield return new WaitUntil(() => confirmed);

        GamePhase resume = GamePhase.Sandbox;
        if (checkpoints && checkpoints.HasCheckpoint)
        {
            resume = checkpoints.Last.phase;
            checkpoints.Restore();
        }
        else
        {
            if (windmill) windmill.SetState(windmill.maxHealth * 0.5f, 0f);
        }
        if (lifeSupport)
        {
            lifeSupport.active = true;
            if (lifeSupport.Value01 < lifeSupportFloorOnRestore) lifeSupport.Set(lifeSupport.max * lifeSupportFloorOnRestore);
        }
        if (daffodil) daffodil.Refill();
        if (resume < GamePhase.Sandbox) resume = GamePhase.Sandbox;

        _lowWarned = false;
        _handlingGameOver = false;
        SetControl(true);
        _flow = StartCoroutine(RunFlow(resume));
    }

    // ---------------- helpers ----------------

    void SetControl(bool enabled)
    {
        PlayerControlEnabled = enabled;
        if (daffodil) daffodil.scanEnabled = enabled;
    }

    void Objective(string text)
    {
        CurrentObjective = text ?? "";
        if (hud) hud.SetObjective(CurrentObjective);
    }

    void Toast(string text, float seconds = 2.5f)
    {
        if (hud) hud.Toast(text, seconds);
        else Debug.Log("[GameFlow] " + text);
    }

    /// <summary>
    /// Walks a Mycari back into the roam area and hands it over to normal roaming once it
    /// arrives. Yields until it is home (or gives up), so a caller can wait for the exit.
    /// </summary>
    IEnumerator SendHome(NPCFlocker npc)
    {
        if (npc == null) yield break;

        Vector3 home = npcManager ? npcManager.GetRandomNavPointInside() : npc.transform.position;
        bool arrived = false;
        npc.ScriptedMoveTo(home, 2.5f, () => arrived = true);

        float deadline = Time.time + 30f;
        yield return new WaitUntil(() => arrived || Time.time >= deadline || npc == null);

        if (npc != null) npc.ScriptedRelease();
        if (npc == _tutorialMycari) _tutorialMycari = null;
    }

    IEnumerator Say(DialogueLine[] lines)
    {
        if (lines == null || lines.Length == 0) yield break;
        if (!dialogue)
        {
            foreach (var l in lines) Debug.Log($"[Dialogue] {l.speaker}: {l.text}");
            yield break;
        }
        bool done = false;
        dialogue.Play(lines, () => done = true);
        yield return new WaitUntil(() => done);
        yield return null; // swallow the advance key so it doesn't leak into gameplay
    }

    IEnumerator PlayVideo(VideoClip clip, string placeholderTitle)
    {
        if (!video) yield break;
        bool done = false;
        video.Play(clip, placeholderTitle, () => done = true);
        yield return new WaitUntil(() => done);
    }

    /// <summary>A point a fixed distance from the player, biased away from the windmill so the first
    /// tornado is clearly visible but not yet a threat.</summary>
    Vector3 StrikePointNearPlayer(float distance)
    {
        Vector3 p = player ? player.transform.position : Vector3.zero;
        Vector3 dir = windmill ? (p - windmill.transform.position) : Vector3.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        Vector3 target = p + dir.normalized * distance;
        if (NavMesh.SamplePosition(target, out var hit, 10f, NavMesh.AllAreas)) target = hit.position;
        return target;
    }

    void PlayBunkerMusic()
    {
        if (!_music) return;
        _music.clip = bunkerMusic ? bunkerMusic : ProceduralMelody();
        if (_music.clip) _music.Play();
    }

    /// <summary>
    /// A short pentatonic loop so the "new music" beat is audible before a real track exists.
    /// </summary>
    static AudioClip ProceduralMelody()
    {
        const int rate = 44100;
        float[] notes = { 261.63f, 329.63f, 392.00f, 440.00f, 523.25f, 440.00f, 392.00f, 329.63f,
                          293.66f, 392.00f, 440.00f, 523.25f, 587.33f, 523.25f, 440.00f, 392.00f };
        const float noteLen = 0.32f;
        int perNote = Mathf.RoundToInt(rate * noteLen);
        var data = new float[perNote * notes.Length];
        for (int n = 0; n < notes.Length; n++)
        {
            float f = notes[n];
            for (int i = 0; i < perNote; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 6f) * Mathf.Min(1f, i / 200f);
                float s = Mathf.Sin(2f * Mathf.PI * f * t) * 0.6f
                        + Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.2f
                        + Mathf.Sin(2f * Mathf.PI * f * 0.5f * t) * 0.2f;
                data[n * perNote + i] = s * env * 0.35f;
            }
        }
        var clip = AudioClip.Create("MycariMelody", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
