using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Translates the game's existing events (and a few polled state changes that have no event) into
    /// <see cref="GameSignals"/>. Objects that appear later (auto-created flow, spawned Mycari, new sites)
    /// are picked up by a rescan every half second. Purely additive: it never changes gameplay state.
    /// </summary>
    [OverhaulModule(order: -100)]
    public class SignalBridge : MonoBehaviour
    {
        public static SignalBridge Instance { get; private set; }

        // ---- cached references (public so other overhaul modules can reuse them) --------------------------
        public PlayerController Player { get; private set; }
        public MartianSequencePuzzle Puzzle { get; private set; }
        public MushroomFieldTrigger Grove { get; private set; }
        public Obelisk Obelisk { get; private set; }
        public TornadoCalamity Tornado { get; private set; }
        public TaskSite Windmill { get; private set; }

        readonly HashSet<Object> _hooked = new HashSet<Object>();
        readonly Dictionary<NPCFlocker, NpcTrack> _npcs = new Dictionary<NPCFlocker, NpcTrack>();
        readonly Dictionary<TaskSite, SiteTrack> _sites = new Dictionary<TaskSite, SiteTrack>();
        readonly List<NPCFlocker> _gone = new List<NPCFlocker>();
        float _rescanAt;
        string _lastObjective;
        Transform _home;
        bool _homeSearched;
        int _lastShards = -1;

        // footsteps
        Vector3 _lastFootPos;
        float _strideAccum;
        bool _footInit;

        class NpcTrack
        {
            public NPCState state;
            public TaskSite task;
            public bool burst;
            public Vector3 lastPos;
            public MycariAnimator anim;
            public bool animSearched;
        }

        class SiteTrack
        {
            public float health;
            public float shield;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameFlowManager.OnPhaseChanged -= OnPhase;
        }

        void Start()
        {
            GameFlowManager.OnPhaseChanged -= OnPhase;
            GameFlowManager.OnPhaseChanged += OnPhase;
            Rescan();
        }

        void OnPhase(GamePhase p) => GameSignals.RaisePhaseChanged(p);

        void Update()
        {
            if (Time.unscaledTime >= _rescanAt)
            {
                // Fast while the scene is settling (auto-created flow, first spawns), then relaxed.
                _rescanAt = Time.unscaledTime + (Time.timeSinceLevelLoad < 20f ? 0.5f : 2f);
                Rescan();
            }
            PollNpcs();
            PollSites();
            PollFootsteps();
            PollObjective();
        }

        // ---------------------------------------------------------------------------------------------------
        void Rescan()
        {
            if (Player == null) Player = FindAnyObjectByType<PlayerController>();
            if (Player != null && Hook(Player))
            {
                Player.OnShardsChanged += n =>
                {
                    if (_lastShards >= 0 && n > _lastShards) GameSignals.RaiseShardCollected(Player.transform.position);
                    _lastShards = n;
                };
                _lastShards = Player.Shards;
            }

            if (Puzzle == null) Puzzle = FindAnyObjectByType<MartianSequencePuzzle>();
            if (Puzzle != null && Hook(Puzzle))
            {
                Puzzle.OnSuccess += () => GameSignals.RaisePuzzleSolved(GroveCenter());
                Puzzle.OnFail += () => GameSignals.RaisePuzzleFailed(GroveCenter());
                Puzzle.OnPlayerTurnStarted += GameSignals.RaisePuzzlePlayerTurn;
            }

            if (Grove == null) Grove = FindAnyObjectByType<MushroomFieldTrigger>();
            if (Grove != null && Hook(Grove))
                Grove.OnPlayerEntered += () => GameSignals.RaiseGroveEntered(Grove.Center);

            if (Obelisk == null) Obelisk = FindAnyObjectByType<Obelisk>();
            if (Obelisk != null && Hook(Obelisk))
                Obelisk.OnStageChanged += st => GameSignals.RaiseObeliskStageChanged(st, Obelisk.transform.position);

            if (Tornado == null) Tornado = FindAnyObjectByType<TornadoCalamity>();
            if (Tornado != null && Hook(Tornado))
            {
                Tornado.OnSpawned += () => GameSignals.RaiseTornadoSpawned(Tornado.Position);
                Tornado.OnEnded += () => GameSignals.RaiseTornadoEnded(Tornado.Position);
            }

            var life = LifeSupport.Instance;
            if (life != null && Hook(life))
            {
                life.OnChanged += _ => GameSignals.RaiseLifeSupportChanged(life.Value01);
                life.OnLow += GameSignals.RaiseLifeSupportLow;
                life.OnDepleted += GameSignals.RaiseLifeSupportDepleted;
            }

            var daffodil = FollowPlayer.Instance;
            if (daffodil != null && Hook(daffodil))
            {
                daffodil.OnScanStarted += GameSignals.RaiseScanStarted;
                daffodil.OnScanStopped += GameSignals.RaiseScanStopped;
                daffodil.OnFellAsleep += GameSignals.RaiseDaffodilShutdown;
                daffodil.OnWokeUp += GameSignals.RaiseDaffodilBoot;
            }

            var dialogue = DialogueSystem.Instance;
            if (dialogue != null && Hook(dialogue))
            {
                dialogue.OnLineStarted += line => { if (line != null) GameSignals.RaiseDialogueLine(line.speaker, line.text); };
                dialogue.OnDialogueEnded += GameSignals.RaiseDialogueEnded;
            }

            foreach (var cp in FindObjectsByType<CheckpointManager>(FindObjectsSortMode.None))
            {
                if (!Hook(cp)) continue;
                cp.OnSaved += GameSignals.RaiseCheckpointSaved;
                cp.OnRestored += GameSignals.RaiseCheckpointRestored;
            }

            foreach (var b in FindObjectsByType<BunkerTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (Hook(b)) b.OnEntered += GameSignals.RaiseBunkerEntered;

            var tm = TaskManager.Instance;
            if (tm != null)
            {
                foreach (var s in tm.Sites) TrackSite(s);
            }
            else
            {
                foreach (var s in FindObjectsByType<TaskSite>(FindObjectsSortMode.None)) TrackSite(s);
            }

            if (!_homeSearched)
            {
                _homeSearched = true;
                var h = GameObject.Find("MycariHome");
                if (h != null) _home = h.transform;
            }

            var npcm = NPCManager.Instance;
            if (npcm != null)
            {
                foreach (var n in npcm.All)
                    if (n != null && !_npcs.ContainsKey(n))
                        _npcs[n] = new NpcTrack { state = n.state, task = n.CurrentTask, lastPos = n.transform.position };
            }
        }

        bool Hook(Object o) => o != null && _hooked.Add(o);

        void TrackSite(TaskSite s)
        {
            if (s == null || _sites.ContainsKey(s)) return;
            _sites[s] = new SiteTrack { health = s.health, shield = s.shield };
            if (Windmill == null && (s.type == TaskType.PowerPlant || s.name.ToLowerInvariant().Contains("wind"))) Windmill = s;
            s.OnDepleted += site => GameSignals.RaiseSiteDepleted(site);
            s.OnFullyRepaired += site => GameSignals.RaiseSiteRepaired(site);
            s.OnShieldBroken += site => GameSignals.RaiseSiteShieldBroken(site);
        }

        Vector3 GroveCenter()
        {
            if (Grove != null) return Grove.Center;
            if (Puzzle != null) return Puzzle.transform.position;
            return Player != null ? Player.transform.position : Vector3.zero;
        }

        // ---------------------------------------------------------------------------------------------------
        void PollSites()
        {
            foreach (var kv in _sites)
            {
                var s = kv.Key;
                if (s == null) continue;
                var t = kv.Value;
                float dh = s.health - t.health;
                if (Mathf.Abs(dh) > 0.01f)
                {
                    GameSignals.RaiseSiteHealthDelta(s, dh);
                    if (dh < -0.5f && Tornado != null && Tornado.IsActive)
                    {
                        Vector3 d = Tornado.Position - s.transform.position; d.y = 0f;
                        if (d.magnitude < 8f) GameSignals.RaiseTornadoHit(s.transform.position);
                    }
                    t.health = s.health;
                }
                float ds = s.shield - t.shield;
                if (ds > 1f) GameSignals.RaiseSiteShieldBuilt(s);
                if (Mathf.Abs(ds) > 0.01f) t.shield = s.shield;
            }
        }

        void PollNpcs()
        {
            _gone.Clear();
            foreach (var kv in _npcs)
            {
                var n = kv.Key;
                var t = kv.Value;
                if (n == null || !n.isActiveAndEnabled)
                {
                    _gone.Add(n);
                    continue;
                }
                t.lastPos = n.transform.position;
                NPCState s = n.state;
                if (s != t.state)
                {
                    OnNpcState(n, t.state, s, t.task);
                    t.state = s;
                }
                t.task = n.CurrentTask;

                // Breaker hits: rising edge of the attack animation's burst frame while working a dismantle.
                if (!t.animSearched) { t.anim = n.GetComponentInChildren<MycariAnimator>(); t.animSearched = true; }
                var anim = t.anim;
                bool burst = anim != null && anim.IsOnBurstFrame && s == NPCState.Work && n.CurrentCommand == TaskCommand.Dismantle;
                if (burst && !t.burst)
                {
                    Vector3 hit = n.CurrentTask != null ? n.CurrentTask.transform.position : n.transform.position;
                    GameSignals.RaiseMycariStrike(n, Vector3.Lerp(n.transform.position, hit, 0.6f) + Vector3.up * 0.8f);
                }
                t.burst = burst;
            }
            foreach (var n in _gone)
            {
                if (_npcs.TryGetValue(n, out var t))
                {
                    // Gone near the door = it walked home through the dome door. Anywhere else (checkpoint rollback,
                    // population cleanup) is not a departure.
                    Vector3 home = _home != null ? _home.position
                                 : OverhaulGeneratedRoot.Instance != null ? OverhaulGeneratedRoot.Instance.home : t.lastPos;
                    Vector3 d = t.lastPos - home; d.y = 0f;
                    if (d.magnitude < 7f) GameSignals.RaiseMycariLeftHome(n, t.lastPos);
                }
                _npcs.Remove(n);
            }
        }

        void OnNpcState(NPCFlocker n, NPCState from, NPCState to, TaskSite lastTask)
        {
            switch (to)
            {
                case NPCState.FollowToDecision:
                    if (from == NPCState.Roam || from == NPCState.ReturnToRoam || from == NPCState.DeclineCooldown)
                        GameSignals.RaiseMycariRecruited(n);
                    break;
                case NPCState.DeclineCooldown:
                    GameSignals.RaiseMycariDeclined(n);
                    break;
                case NPCState.FollowToTask:
                    GameSignals.RaiseMycariCommanded(n, n.CurrentTask, n.CurrentCommand);
                    break;
                case NPCState.Work:
                    GameSignals.RaiseMycariStartedWork(n, n.CurrentTask);
                    break;
                case NPCState.ReturnToRoam:
                case NPCState.Roam:
                    if (from == NPCState.FollowToDecision || from == NPCState.AwaitDecision || from == NPCState.FollowApproved)
                        GameSignals.RaiseMycariDismissed(n);
                    break;
            }
            if (from == NPCState.Work && to != NPCState.Work)
                GameSignals.RaiseMycariFinishedWork(n, lastTask);
        }

        void PollFootsteps()
        {
            if (Player == null) return;
            var cc = Player.GetComponent<CharacterController>();
            Vector3 pos = Player.transform.position;
            if (!_footInit) { _lastFootPos = pos; _footInit = true; return; }
            Vector3 d = pos - _lastFootPos; d.y = 0f;
            _lastFootPos = pos;
            float dist = d.magnitude;
            if (dist > 2f) { _strideAccum = 0f; return; } // teleport / checkpoint
            bool grounded = cc == null || cc.isGrounded || Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, 1.5f);
            if (!grounded || dist < 1e-4f) return;
            float speed = dist / Mathf.Max(Time.deltaTime, 1e-4f);
            float speed01 = Mathf.InverseLerp(1.5f, 6f, speed);
            float stride = Mathf.Lerp(0.75f, 1.25f, speed01);
            _strideAccum += dist;
            if (_strideAccum >= stride)
            {
                _strideAccum -= stride;
                Vector3 feet = pos;
                if (cc != null) feet = Player.transform.TransformPoint(cc.center) - Vector3.up * (cc.height * 0.5f * Player.transform.lossyScale.y);
                GameSignals.RaiseFootstep(feet, speed01);
            }
        }

        void PollObjective()
        {
            var gfm = GameFlowManager.Instance;
            if (gfm == null) return;
            string o = gfm.CurrentObjective;
            if (o != _lastObjective)
            {
                bool first = _lastObjective == null;
                _lastObjective = o;
                if (!first && !string.IsNullOrEmpty(o)) GameSignals.RaiseObjectiveChanged(o);
            }
        }
    }
}
