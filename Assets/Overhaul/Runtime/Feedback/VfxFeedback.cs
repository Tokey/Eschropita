using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// A visual answer for every interaction (built from <see cref="VfxKit"/>, with <see cref="CameraJuice"/> on the
    /// big beats), so every action feels physical:
    ///   footstep dust (spores in the grove) · note-coloured light and ripples under singing mushrooms · spore
    ///   fountain + light pulse + hit-stop when the song is solved · role-coloured rings, rising motes and a glowing
    ///   tether to the Mycari following you · beacon beams at commanded sites · teal repair streams from working
    ///   Fixers · sparks and debris on Breaker hits · bursts when sites recover, spark showers and smoke when they
    ///   fail · amber shimmer / shatter for shields · shard sparkle trails · obelisk shockwaves · tornado dust waves
    ///   · Daffodil's expanding scan rings · a light column at checkpoints · green shimmer when a Mycari goes home.
    /// Mycari also squash-and-stretch when they answer you.
    /// </summary>
    [OverhaulModule(40)]
    public class VfxFeedback : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.84f, 0.45f);
        static readonly Color Mint = new Color(0.55f, 1f, 0.8f);
        static readonly Color Dust = new Color(0.62f, 0.42f, 0.3f, 0.55f);
        // Footstep dust: a touch darker and more opaque than the ground's own colour so it reads on it.
        static readonly Color StepDust = new Color(0.5f, 0.3f, 0.2f, 0.8f);
        static readonly Color StepDustHaze = new Color(0.7f, 0.5f, 0.38f, 0.5f);
        static readonly Color StepDustGrove = new Color(0.55f, 0.5f, 0.6f, 0.45f);
        static readonly Color Ember = new Color(1f, 0.55f, 0.18f);
        static readonly Color Violet = new Color(0.7f, 0.4f, 1f);

        LineRenderer _tether;
        readonly Dictionary<NPCFlocker, TaskSite> _workers = new Dictionary<NPCFlocker, TaskSite>();
        readonly List<NPCFlocker> _gone = new List<NPCFlocker>();
        float _nextStream, _nextScanRing;
        bool _scanning;

        void OnEnable()
        {
            GameSignals.Footstep += OnFootstep;
            GameSignals.Interacted += OnInteracted;
            GameSignals.PadSang += OnPadSang;
            GameSignals.PadPressed += OnPadPressed;
            GameSignals.PuzzlePlayerTurn += OnPlayerTurn;
            GameSignals.PuzzleSolved += OnSolved;
            GameSignals.PuzzleFailed += OnFailed;
            GameSignals.MycariRecruited += OnRecruited;
            GameSignals.MycariDeclined += OnDeclined;
            GameSignals.MycariDismissed += OnDismissed;
            GameSignals.MycariCommanded += OnCommanded;
            GameSignals.MycariStartedWork += OnStartedWork;
            GameSignals.MycariFinishedWork += OnFinishedWork;
            GameSignals.MycariStrike += OnStrike;
            GameSignals.MycariLeftHome += OnLeftHome;
            GameSignals.MycariAttuned += OnAttuned;
            GameSignals.MycariAsked += OnRecruited;
            GameSignals.MycariPersuaded += OnPersuaded;
            GameSignals.SiteRepaired += OnRepaired;
            GameSignals.SiteDepleted += OnDepleted;
            GameSignals.SiteShieldBuilt += OnShieldBuilt;
            GameSignals.SiteShieldBroken += OnShieldBroken;
            GameSignals.ShardCollected += OnShard;
            GameSignals.ObeliskStageChanged += OnObelisk;
            GameSignals.TornadoSpawned += OnTornado;
            GameSignals.TornadoHit += OnTornadoHit;
            GameSignals.ScanStarted += OnScanStart;
            GameSignals.ScanStopped += OnScanStop;
            GameSignals.DaffodilShutdown += OnShutdown;
            GameSignals.DaffodilBoot += OnBoot;
            GameSignals.CheckpointSaved += OnCheckpoint;
            GameSignals.BunkerEntered += OnBunker;
            GameSignals.GroveEntered += OnGrove;
            GameSignals.LifeSupportDepleted += OnDepletedLife;
        }

        void OnDisable()
        {
            GameSignals.Footstep -= OnFootstep;
            GameSignals.Interacted -= OnInteracted;
            GameSignals.PadSang -= OnPadSang;
            GameSignals.PadPressed -= OnPadPressed;
            GameSignals.PuzzlePlayerTurn -= OnPlayerTurn;
            GameSignals.PuzzleSolved -= OnSolved;
            GameSignals.PuzzleFailed -= OnFailed;
            GameSignals.MycariRecruited -= OnRecruited;
            GameSignals.MycariDeclined -= OnDeclined;
            GameSignals.MycariDismissed -= OnDismissed;
            GameSignals.MycariCommanded -= OnCommanded;
            GameSignals.MycariStartedWork -= OnStartedWork;
            GameSignals.MycariFinishedWork -= OnFinishedWork;
            GameSignals.MycariStrike -= OnStrike;
            GameSignals.MycariLeftHome -= OnLeftHome;
            GameSignals.MycariAttuned -= OnAttuned;
            GameSignals.MycariAsked -= OnRecruited;
            GameSignals.MycariPersuaded -= OnPersuaded;
            GameSignals.SiteRepaired -= OnRepaired;
            GameSignals.SiteDepleted -= OnDepleted;
            GameSignals.SiteShieldBuilt -= OnShieldBuilt;
            GameSignals.SiteShieldBroken -= OnShieldBroken;
            GameSignals.ShardCollected -= OnShard;
            GameSignals.ObeliskStageChanged -= OnObelisk;
            GameSignals.TornadoSpawned -= OnTornado;
            GameSignals.TornadoHit -= OnTornadoHit;
            GameSignals.ScanStarted -= OnScanStart;
            GameSignals.ScanStopped -= OnScanStop;
            GameSignals.DaffodilShutdown -= OnShutdown;
            GameSignals.DaffodilBoot -= OnBoot;
            GameSignals.CheckpointSaved -= OnCheckpoint;
            GameSignals.BunkerEntered -= OnBunker;
            GameSignals.GroveEntered -= OnGrove;
            GameSignals.LifeSupportDepleted -= OnDepletedLife;
            if (_tether != null) _tether.enabled = false;
        }

        // ---- helpers -------------------------------------------------------------------------------------
        static Color RoleColor(NPCFlocker n)
        {
            if (n == null) return Gold;
            // Before the player knows a Mycari's role (scan or attunement) its colour stays a neutral gold.
            bool known = TripDirector.IsAttuned(n) || (GameFlowManager.Instance != null && GameFlowManager.Instance.MycariObey);
            return known ? OverhaulPalette.ForRole(n.role) : Gold;
        }

        static Color NoteColor(int pad)
        {
            var bridge = SignalBridge.Instance;
            var puzzle = bridge != null ? bridge.Puzzle : null;
            if (puzzle != null && puzzle.pads != null && pad >= 0 && pad < puzzle.pads.Length && puzzle.pads[pad] != null)
            {
                var mp = puzzle.pads[pad].GetComponent<MushroomPad>();
                if (mp != null) return mp.NoteColor;
            }
            return OverhaulPalette.SporeCyan;
        }

        static Vector3 Ground(Vector3 p) { p.y = OverhaulPrimitives.GroundY(p); return p; }

        // ---- player & puzzle -----------------------------------------------------------------------------
        void OnFootstep(Vector3 p, float speed01)
        {
            if (TripDirector.InGrove)
            {
                VfxKit.Spores(p + Vector3.up * 0.1f, 2 + (speed01 > 0.5f ? 2 : 0), Color.Lerp(OverhaulPalette.SporeCyan, OverhaulPalette.SporeViolet, Random.value), 0.3f);
                VfxKit.Dust(p + Vector3.up * 0.05f, 2, StepDustGrove, 0.4f, 0.4f);
                return;
            }
            // A proper puff of red regolith per step: a low, wide kick plus a few bigger, slower clouds that hang
            // for a moment, dense enough to read against the same-coloured ground.
            VfxKit.Dust(p + Vector3.up * 0.05f, 4 + Mathf.RoundToInt(speed01 * 3f), StepDust, 0.5f + 0.25f * speed01, 0.9f + 1.2f * speed01);
            VfxKit.Dust(p + Vector3.up * 0.15f, 2 + Mathf.RoundToInt(speed01 * 2f), StepDustHaze, 0.9f + 0.35f * speed01, 0.35f);
        }

        void OnInteracted(Vector3 p)
        {
            VfxKit.Ring(p, Gold, 1.4f, 0.4f, 2f);
            VfxKit.Sparks(p + Vector3.up * 0.8f, 6, Gold, 2.5f);
        }

        void OnPadSang(int i, Vector3 p)
        {
            Color c = NoteColor(i);
            VfxKit.Ring(p, c, 3.2f, 0.9f, 3f);
            VfxKit.Flash(p + Vector3.up * 1.2f, c, 2600f, 0.55f, 9f);
            VfxKit.Spores(p + Vector3.up * 0.4f, 10, c, 0.6f);
        }

        void OnPadPressed(int i, Vector3 p, bool ok)
        {
            Color c = NoteColor(i);
            if (ok)
            {
                VfxKit.Ring(p, c, 2.4f, 0.5f, 3.5f, 0.5f);
                VfxKit.Motes(p + Vector3.up * 0.6f, 12, c, 0.5f, 1.8f);
                VfxKit.Flash(p + Vector3.up, c, 1800f, 0.35f, 7f);
            }
            else
            {
                VfxKit.Dust(p + Vector3.up * 0.3f, 8, new Color(0.45f, 0.3f, 0.6f, 0.6f), 0.5f, 1.2f);
                VfxKit.Ring(p, Violet, 1.6f, 0.4f, 1.2f);
                CameraJuice.Impulse(p, 0.25f);
            }
        }

        void OnPlayerTurn()
        {
            var bridge = SignalBridge.Instance;
            if (bridge != null && bridge.Player != null) VfxKit.Ring(bridge.Player.transform.position, OverhaulPalette.SporeCyan, 3f, 1f, 1.6f, 0.5f);
        }

        void OnSolved(Vector3 p)
        {
            for (int k = 0; k < 4; k++) VfxKit.Spores(p + Vector3.up * 0.3f, 18, NoteColor(k), 2.5f);
            VfxKit.Motes(p + Vector3.up * 0.5f, 40, Color.white, 2f, 3.5f, 0.16f);
            VfxKit.Ring(p, OverhaulPalette.SporeCyan, 13f, 1.6f, 4f, 1f);
            VfxKit.Ring(p, OverhaulPalette.SporeViolet, 9f, 1.2f, 3f, 0.5f);
            VfxKit.Flash(p + Vector3.up * 2f, new Color(0.7f, 0.95f, 1f), 9000f, 1.4f, 22f);
            CameraJuice.Impulse(p, 0.6f);
            CameraJuice.HitStop(0.06f);
        }

        void OnFailed(Vector3 p)
        {
            VfxKit.Dust(p + Vector3.up * 0.2f, 14, new Color(0.4f, 0.3f, 0.55f, 0.5f), 0.8f, 1.5f);
            VfxKit.Ring(p, Violet, 5f, 0.7f, 1.2f);
        }

        // ---- Mycari ----------------------------------------------------------------------------------------
        void OnRecruited(NPCFlocker n)
        {
            if (n == null) return;
            Color c = RoleColor(n);
            VfxKit.Ring(n.transform.position, c, 2.4f, 0.7f, 3f);
            VfxKit.Motes(n.transform.position + Vector3.up * 0.6f, 16, c, 0.5f, 1.6f);
            CameraJuice.Punch(n.transform, 0.2f);
        }

        void OnPersuaded(NPCFlocker n)
        {
            if (n == null) return;
            Color c = RoleColor(n);
            VfxKit.Ring(n.transform.position, c, 3.4f, 0.9f, 3.5f);
            VfxKit.Spores(n.transform.position + Vector3.up * 0.5f, 18, c, 1f);
            CameraJuice.Punch(n.transform, 0.24f, 0.4f);
        }

        void OnDeclined(NPCFlocker n)
        {
            if (n == null) return;
            VfxKit.Dust(n.transform.position + Vector3.up * 0.3f, 8, new Color(0.5f, 0.5f, 0.5f, 0.5f), 0.45f, 1f);
            VfxKit.Ring(n.transform.position, new Color(0.6f, 0.6f, 0.65f), 1.4f, 0.4f, 1f);
            CameraJuice.Punch(n.transform, -0.14f, 0.3f);
        }

        void OnDismissed(NPCFlocker n)
        {
            var bridge = SignalBridge.Instance;
            if (n == null) return;
            Vector3 a = bridge != null && bridge.Player != null ? bridge.Player.transform.position : n.transform.position;
            VfxKit.Motes(Vector3.Lerp(a, n.transform.position, 0.5f) + Vector3.up, 10, Gold, 0.5f, 1f);
            VfxKit.Ring(n.transform.position, Gold, 1.6f, 0.4f, 1.4f);
        }

        void OnCommanded(NPCFlocker n, TaskSite site, TaskCommand cmd)
        {
            Color c = cmd == TaskCommand.Repair ? OverhaulPalette.RoleFixer : OverhaulPalette.RoleBreaker;
            if (site != null)
            {
                VfxKit.Beam(site.transform.position, c, 11f, 2f, 1.8f, 2.4f);
                VfxKit.Ring(site.transform.position, c, 5f, 0.9f, 2.5f, 1f);
            }
            if (n != null) CameraJuice.Punch(n.transform, 0.22f);
        }

        void OnStartedWork(NPCFlocker n, TaskSite s)
        {
            if (n == null || s == null) return;
            if (n.CurrentCommand == TaskCommand.Repair) _workers[n] = s;
            VfxKit.Ring(n.transform.position, RoleColor(n), 1.8f, 0.5f, 2f);
        }

        void OnFinishedWork(NPCFlocker n, TaskSite s) { if (n != null) _workers.Remove(n); }

        void OnStrike(NPCFlocker n, Vector3 p)
        {
            VfxKit.Sparks(p, 18, Ember, 7f);
            VfxKit.Dust(p, 6, new Color(0.35f, 0.25f, 0.2f, 0.7f), 0.5f, 2f);
            VfxKit.Flash(p, Ember, 1500f, 0.25f, 6f);
            CameraJuice.Impulse(p, 0.22f);
            if (n != null) CameraJuice.Punch(n.transform, 0.12f, 0.25f);
        }

        void OnLeftHome(NPCFlocker n, Vector3 p)
        {
            Color g = OverhaulPalette.RoleWanderer;
            VfxKit.Motes(p + Vector3.up * 0.3f, 30, g, 0.8f, 2.6f);
            VfxKit.Ring(p, g, 4f, 1f, 3f);
            VfxKit.Flash(p + Vector3.up * 1.5f, g, 4000f, 0.9f, 12f);
            VfxKit.Beam(p, g, 9f, 1.4f, 1.2f, 2f);
        }

        void OnAttuned(NPCFlocker n)
        {
            if (n == null) return;
            Color c = OverhaulPalette.ForRole(n.role);
            VfxKit.Beam(n.transform.position, c, 7f, 1.8f, 1.4f, 2.6f);
            VfxKit.Ring(n.transform.position, c, 4f, 1.1f, 3.5f, 0.5f);
            VfxKit.Spores(n.transform.position + Vector3.up * 0.5f, 24, c, 1.2f);
            CameraJuice.Punch(n.transform, 0.25f, 0.45f);
        }

        // ---- sites -----------------------------------------------------------------------------------------
        void OnRepaired(TaskSite s)
        {
            if (s == null) return;
            Vector3 p = s.transform.position;
            VfxKit.Motes(p + Vector3.up * 2f, 40, Mint, 2.5f, 2.5f, 0.15f);
            VfxKit.Ring(p, Mint, 9f, 1.3f, 3.5f, 1f);
            VfxKit.Flash(p + Vector3.up * 5f, Mint, 9000f, 1.1f, 20f);
            CameraJuice.Impulse(p, 0.4f);
        }

        void OnDepleted(TaskSite s)
        {
            if (s == null) return;
            Vector3 p = s.transform.position + Vector3.up * 6f;
            VfxKit.Sparks(p, 40, Ember, 8f, 0.4f);
            VfxKit.Smoke(p, 10, 1.4f);
            VfxKit.Flash(p, new Color(1f, 0.35f, 0.2f), 7000f, 0.8f, 18f);
            CameraJuice.Impulse(s.transform.position, 0.5f);
        }

        void OnShieldBuilt(TaskSite s)
        {
            if (s == null) return;
            Vector3 p = s.transform.position + Vector3.up * 3f;
            VfxKit.Motes(p, 30, OverhaulPalette.ShardAmber, 2f, 1.5f);
            VfxKit.Ring(s.transform.position, OverhaulPalette.ShardAmber, 6f, 1f, 3f, 1f);
            VfxKit.Flash(p, OverhaulPalette.ShardAmber, 5000f, 0.7f, 14f);
        }

        void OnShieldBroken(TaskSite s)
        {
            if (s == null) return;
            Vector3 p = s.transform.position + Vector3.up * 3f;
            VfxKit.Shards(p, 25, OverhaulPalette.ShardAmber, 6f);
            VfxKit.Flash(p, OverhaulPalette.ShardAmber, 5000f, 0.5f, 14f);
            CameraJuice.Impulse(p, 0.45f);
        }

        void OnShard(Vector3 p)
        {
            var bridge = SignalBridge.Instance;
            VfxKit.Motes(p + Vector3.up * 0.4f, 14, OverhaulPalette.ShardAmber, 0.4f, 1.5f);
            VfxKit.Ring(p, OverhaulPalette.ShardAmber, 1.8f, 0.45f, 3f);
            if (bridge != null && bridge.Player != null)
                VfxKit.Stream(p + Vector3.up * 0.5f, bridge.Player.transform.position + Vector3.up * 1.2f, 10, OverhaulPalette.ShardAmber, 0.5f);
            CameraJuice.Impulse(p, 0.12f);
        }

        void OnObelisk(ObeliskStage st, Vector3 p)
        {
            Vector3 c = p + Vector3.up * 4f;
            switch (st)
            {
                case ObeliskStage.Exposed:
                    VfxKit.Shards(c, 30, Violet, 7f);
                    VfxKit.Ring(p, Violet, 10f, 1.2f, 3f, 1f);
                    VfxKit.Flash(c, Violet, 9000f, 0.8f, 20f);
                    CameraJuice.Impulse(p, 0.6f);
                    break;
                case ObeliskStage.Cracked:
                    VfxKit.Sparks(c, 30, Violet, 6f);
                    VfxKit.Flash(c, Violet, 5000f, 0.5f, 16f);
                    CameraJuice.Impulse(p, 0.45f);
                    break;
                case ObeliskStage.Destroyed:
                    VfxKit.Ring(p, new Color(1f, 0.8f, 1f), 26f, 1.8f, 4f, 2f);
                    VfxKit.Ring(p, Violet, 16f, 1.3f, 3f, 1f);
                    VfxKit.Shards(c, 50, Violet, 10f);
                    VfxKit.Dust(p, 30, new Color(0.45f, 0.3f, 0.25f, 0.7f), 1.6f, 5f);
                    VfxKit.Smoke(c, 14, 2f);
                    VfxKit.Flash(c, new Color(0.95f, 0.8f, 1f), 30000f, 1.6f, 40f);
                    CameraJuice.Impulse(p, 1.5f);
                    CameraJuice.HitStop(0.1f);
                    break;
            }
        }

        void OnTornado(Vector3 p)
        {
            VfxKit.Ring(p, new Color(0.75f, 0.5f, 0.3f), 12f, 1.5f, 1.5f, 1f);
            VfxKit.Dust(p, 30, Dust, 1.4f, 4f);
        }

        void OnTornadoHit(Vector3 p)
        {
            VfxKit.Dust(p, 24, Dust, 1.2f, 5f);
            VfxKit.Sparks(p + Vector3.up * 4f, 14, Ember, 6f);
            CameraJuice.Impulse(p, 0.8f);
        }

        // ---- companion / systems ----------------------------------------------------------------------------
        static Vector3 DaffodilPos()
        {
            var d = FollowPlayer.Instance;
            if (d != null) return d.transform.position;
            var bridge = SignalBridge.Instance;
            return bridge != null && bridge.Player != null ? bridge.Player.transform.position : Vector3.zero;
        }

        void OnScanStart() { _scanning = true; _nextScanRing = 0f; }
        void OnScanStop() => _scanning = false;

        void OnShutdown()
        {
            _scanning = false;
            Vector3 p = DaffodilPos() + Vector3.up * 1.2f;
            VfxKit.Sparks(p, 16, new Color(0.5f, 0.75f, 1f), 3f);
            VfxKit.Smoke(p, 3, 0.4f);
        }

        void OnBoot()
        {
            Vector3 p = DaffodilPos();
            VfxKit.Ring(p, OverhaulPalette.SporeCyan, 2.5f, 0.6f, 3f);
            VfxKit.Motes(p + Vector3.up * 1.2f, 12, OverhaulPalette.SporeCyan, 0.4f, 1.4f);
        }

        void OnCheckpoint()
        {
            var bridge = SignalBridge.Instance;
            if (bridge == null || bridge.Player == null) return;
            Vector3 p = bridge.Player.transform.position;
            VfxKit.Beam(p, new Color(1f, 0.92f, 0.75f), 9f, 1.6f, 1.6f, 2f);
            VfxKit.Ring(p, Gold, 3.5f, 1f, 2f, 0.5f);
        }

        void OnBunker()
        {
            Vector3 p = OverhaulLandmarks.Obelisk;
            for (int k = 0; k < 4; k++) VfxKit.Dust(p + Random.insideUnitSphere * 3f + Vector3.up * 5f, 6, Dust, 0.9f, 0.4f);
        }

        void OnGrove(Vector3 p)
        {
            var bridge = SignalBridge.Instance;
            if (bridge != null && bridge.Player != null)
                VfxKit.Spores(bridge.Player.transform.position + Vector3.up * 0.4f, 20, OverhaulPalette.SporeCyan, 1.4f);
        }

        void OnDepletedLife()
        {
            var bridge = SignalBridge.Instance;
            if (bridge != null && bridge.Player != null) CameraJuice.Impulse(bridge.Player.transform.position, 0.5f);
        }

        // ---- continuous effects ----------------------------------------------------------------------------
        void Update()
        {
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;

            // Daffodil's scan: an expanding cyan ring every ~1.6 s while scanning.
            if (_scanning && Time.time >= _nextScanRing)
            {
                _nextScanRing = Time.time + 1.6f;
                var d = FollowPlayer.Instance;
                float radius = d != null ? d.scanRadius : 8f;
                VfxKit.Ring(DaffodilPos(), OverhaulPalette.SporeCyan, radius, 1.3f, 2.2f, 0.4f);
            }

            // Repair streams from working Fixers to their site.
            if (Time.time >= _nextStream && _workers.Count > 0)
            {
                _nextStream = Time.time + 0.18f;
                _gone.Clear();
                foreach (var kv in _workers)
                {
                    if (kv.Key == null || kv.Value == null || kv.Key.state != NPCState.Work) { _gone.Add(kv.Key); continue; }
                    Vector3 to = kv.Value.transform.position + Vector3.up * Random.Range(1f, 5f);
                    VfxKit.Stream(kv.Key.transform.position + Vector3.up * 0.8f, to, 2, OverhaulPalette.SporeTeal, 0.7f);
                }
                foreach (var n in _gone) _workers.Remove(n);
            }

            UpdateTether(player);
        }

        void UpdateTether(PlayerController player)
        {
            var follower = player != null ? player.CurrentFollower : null;
            bool show = follower != null && follower.isActiveAndEnabled &&
                        (follower.state == NPCState.FollowToDecision || follower.state == NPCState.AwaitDecision || follower.state == NPCState.FollowApproved);
            if (!show)
            {
                if (_tether != null) _tether.enabled = false;
                return;
            }
            if (_tether == null)
            {
                var go = new GameObject("Follower Tether");
                go.transform.SetParent(transform, false);
                _tether = go.AddComponent<LineRenderer>();
                _tether.positionCount = 14;
                _tether.useWorldSpace = true;
                _tether.textureMode = LineTextureMode.Stretch;
                _tether.alignment = LineAlignment.View;
                _tether.numCapVertices = 2;
                _tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tether.receiveShadows = false;
                if (VfxKit.Instance != null) _tether.sharedMaterial = VfxKit.Instance.LineMaterial;
            }
            _tether.enabled = true;
            bool approved = follower.state == NPCState.FollowApproved;
            Color c = RoleColor(follower);
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * (approved ? 6f : 3f));
            Color cc = c * (approved ? 1f : 0.7f) * pulse;
            cc.a = 1f;
            _tether.startColor = cc;
            _tether.endColor = cc;
            float w = approved ? 0.14f : 0.09f;
            _tether.startWidth = w;
            _tether.endWidth = w * 0.7f;
            Vector3 a = player.transform.position + Vector3.up * 1.1f;
            Vector3 b = follower.transform.position + Vector3.up * 0.9f;
            float dist = Vector3.Distance(a, b);
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                Vector3 p = Vector3.Lerp(a, b, t);
                float arch = Mathf.Sin(t * Mathf.PI) * Mathf.Min(1.2f, dist * 0.15f);
                float wave = Mathf.Sin(t * 9f - Time.time * 4f) * 0.08f * Mathf.Sin(t * Mathf.PI);
                _tether.SetPosition(i, p + Vector3.up * (arch + wave));
            }
        }
    }
}
