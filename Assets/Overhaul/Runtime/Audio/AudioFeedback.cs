using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Gives every interaction a sound (cue ids from the sound library):
    /// footsteps · recruiting / declining / dismissing / commanding Mycari, who answer in their role's mushroom
    /// phrase · fungal "knitting" loops while Fixers repair and sparkles as health climbs · Breaker hits ·
    /// turbine down / spin-up · shard chimes that climb the F-major triad on quick pickups · shields · tornado
    /// gusts and impacts · Daffodil's scanner, shutdown and boot · life-support alarm and recovery · checkpoints,
    /// toasts, objectives · lore terminals · dialogue text blips per speaker · stingers for the big beats.
    /// </summary>
    [OverhaulModule(11)]
    public class AudioFeedback : MonoBehaviour
    {
        readonly Dictionary<TaskSite, AudioSource> _repairLoops = new Dictionary<TaskSite, AudioSource>();
        readonly Dictionary<TaskSite, int> _repairers = new Dictionary<TaskSite, int>();
        readonly Dictionary<TaskSite, float> _nextTick = new Dictionary<TaskSite, float>();
        AudioSource _scanLoop, _alarmLoop;
        int _shardCombo;
        float _shardComboUntil;
        bool _lifeWasLow;
        float _nextGust;

        // dialogue blips
        string _blipCue;
        int _blipChars, _blipDone;
        float _blipNext;

        void OnEnable()
        {
            GameSignals.Footstep += OnFootstep;
            GameSignals.Interacted += OnInteracted;
            GameSignals.MycariRecruited += OnRecruited;
            GameSignals.MycariDeclined += OnDeclined;
            GameSignals.MycariDismissed += OnDismissed;
            GameSignals.MycariCommanded += OnCommanded;
            GameSignals.MycariStartedWork += OnStartedWork;
            GameSignals.MycariFinishedWork += OnFinishedWork;
            GameSignals.MycariStrike += OnStrike;
            GameSignals.MycariLeftHome += OnLeftHome;
            GameSignals.MycariAttuned += OnAttuned;
            GameSignals.PadSang += OnPadSang;
            GameSignals.PadPressed += OnPadPressed;
            GameSignals.PuzzlePlayerTurn += OnPlayerTurn;
            GameSignals.PuzzleSolved += OnSolved;
            GameSignals.PuzzleFailed += OnFailed;
            GameSignals.SiteRepaired += OnRepaired;
            GameSignals.SiteDepleted += OnDepleted;
            GameSignals.SiteHealthDelta += OnHealthDelta;
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
            GameSignals.LifeSupportLow += OnLifeLow;
            GameSignals.LifeSupportDepleted += OnLifeDepleted;
            GameSignals.CheckpointSaved += OnCheckpoint;
            GameSignals.ToastShown += OnToast;
            GameSignals.ObjectiveChanged += OnObjective;
            GameSignals.DialogueLine += OnDialogueLine;
            GameSignals.DialogueEnded += OnDialogueEnded;
            GameSignals.LoreOpened += OnLoreOpened;
            GameSignals.LoreClosed += OnLoreClosed;
            GameSignals.ReportCardShown += OnReport;
            GameSignals.LightPathsRevealed += OnPaths;
            GameSignals.BunkerEntered += OnBunker;
            GameSignals.GroveEntered += OnGrove;
            GameSignals.AssessmentProgress += OnAssessment;
        }

        void OnDisable()
        {
            GameSignals.Footstep -= OnFootstep;
            GameSignals.Interacted -= OnInteracted;
            GameSignals.MycariRecruited -= OnRecruited;
            GameSignals.MycariDeclined -= OnDeclined;
            GameSignals.MycariDismissed -= OnDismissed;
            GameSignals.MycariCommanded -= OnCommanded;
            GameSignals.MycariStartedWork -= OnStartedWork;
            GameSignals.MycariFinishedWork -= OnFinishedWork;
            GameSignals.MycariStrike -= OnStrike;
            GameSignals.MycariLeftHome -= OnLeftHome;
            GameSignals.MycariAttuned -= OnAttuned;
            GameSignals.PadSang -= OnPadSang;
            GameSignals.PadPressed -= OnPadPressed;
            GameSignals.PuzzlePlayerTurn -= OnPlayerTurn;
            GameSignals.PuzzleSolved -= OnSolved;
            GameSignals.PuzzleFailed -= OnFailed;
            GameSignals.SiteRepaired -= OnRepaired;
            GameSignals.SiteDepleted -= OnDepleted;
            GameSignals.SiteHealthDelta -= OnHealthDelta;
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
            GameSignals.LifeSupportLow -= OnLifeLow;
            GameSignals.LifeSupportDepleted -= OnLifeDepleted;
            GameSignals.CheckpointSaved -= OnCheckpoint;
            GameSignals.ToastShown -= OnToast;
            GameSignals.ObjectiveChanged -= OnObjective;
            GameSignals.DialogueLine -= OnDialogueLine;
            GameSignals.DialogueEnded -= OnDialogueEnded;
            GameSignals.LoreOpened -= OnLoreOpened;
            GameSignals.LoreClosed -= OnLoreClosed;
            GameSignals.ReportCardShown -= OnReport;
            GameSignals.LightPathsRevealed -= OnPaths;
            GameSignals.BunkerEntered -= OnBunker;
            GameSignals.GroveEntered -= OnGrove;
            GameSignals.AssessmentProgress -= OnAssessment;
            foreach (var kv in _repairLoops) Sfx.StopLoop(kv.Value, 0.1f);
            _repairLoops.Clear();
            Sfx.StopLoop(_scanLoop, 0.1f);
            Sfx.StopLoop(_alarmLoop, 0.1f);
            _scanLoop = _alarmLoop = null;
        }

        // ---- helpers ---------------------------------------------------------------------------------------
        static string PhraseFor(NPCFlocker n)
        {
            if (n == null) return "phrase.wanderer";
            switch (n.role)
            {
                case NPCRole.Worker: return "phrase.fixer";
                case NPCRole.Attacker: return "phrase.breaker";
                default: return "phrase.wanderer";
            }
        }

        static Vector3 At(Component c) => c != null ? c.transform.position + Vector3.up : Vector3.zero;

        void Speak(NPCFlocker n, float delay, float pitch = 1f)
        {
            if (n == null) return;
            if (delay <= 0f) { Sfx.Play(PhraseFor(n), At(n), 1f, pitch); return; }
            StartCoroutine(Later(delay, () => { if (n != null) Sfx.Play(PhraseFor(n), At(n), 1f, pitch); }));
        }

        System.Collections.IEnumerator Later(float s, System.Action a)
        {
            yield return new WaitForSeconds(s);
            a();
        }

        // ---- player ------------------------------------------------------------------------------------------
        void OnFootstep(Vector3 p, float speed01)
        {
            Sfx.Play("sfx.footstep", p, 0.55f + 0.45f * speed01, 0.95f + 0.12f * speed01);
            if (TripDirector.InGrove && Random.value < 0.3f) Sfx.Play("sfx.spores", p, 0.35f);
        }

        void OnInteracted(Vector3 p) => Sfx.Play("sfx.interact", p, 0.8f);

        // ---- Mycari ------------------------------------------------------------------------------------------
        void OnRecruited(NPCFlocker n) { Sfx.Play("sfx.recruit", At(n)); Speak(n, 0.18f); }
        void OnDeclined(NPCFlocker n) { Sfx.Play("sfx.decline", At(n)); Speak(n, 0.12f, 0.84f); }
        void OnDismissed(NPCFlocker n) => Sfx.Play("sfx.dismiss", At(n), 0.8f);

        void OnCommanded(NPCFlocker n, TaskSite site, TaskCommand cmd)
        {
            Sfx.Play(cmd == TaskCommand.Repair ? "sfx.command.repair" : "sfx.command.dismantle", null, 0.85f);
            Speak(n, 0.25f, cmd == TaskCommand.Repair ? 1.06f : 0.94f);
        }

        void OnStartedWork(NPCFlocker n, TaskSite site)
        {
            if (n == null || site == null || n.CurrentCommand != TaskCommand.Repair) return;
            _repairers.TryGetValue(site, out int c);
            _repairers[site] = c + 1;
            if (!_repairLoops.ContainsKey(site) || _repairLoops[site] == null)
                _repairLoops[site] = Sfx.StartLoop("sfx.repair.loop", site.transform.position + Vector3.up * 1.5f, 1f);
        }

        void OnFinishedWork(NPCFlocker n, TaskSite site)
        {
            if (site == null || !_repairers.TryGetValue(site, out int c)) return;
            c = Mathf.Max(0, c - 1);
            _repairers[site] = c;
            if (c == 0 && _repairLoops.TryGetValue(site, out var s))
            {
                Sfx.StopLoop(s, 0.8f);
                _repairLoops.Remove(site);
            }
        }

        void OnStrike(NPCFlocker n, Vector3 p) => Sfx.Play("sfx.hit", p);

        void OnLeftHome(NPCFlocker n, Vector3 p)
        {
            Sfx.Play("sfx.door", p, 0.9f);
            Sfx.Play("sfx.mycari.leave", p + Vector3.up, 1f);
        }

        void OnAttuned(NPCFlocker n)
        {
            Sfx.Play("sfx.attune", null, 0.9f);
            Speak(n, 0.6f);
        }

        // ---- puzzle ------------------------------------------------------------------------------------------
        void OnPadSang(int i, Vector3 p) => Sfx.Play("sfx.pad.ripple", p, 0.6f, 1f + 0.06f * i);
        void OnPadPressed(int i, Vector3 p, bool ok) { if (ok) Sfx.Play("sfx.spores", p, 0.45f, 1f + 0.08f * i); }
        void OnPlayerTurn() => Sfx.Play("ui.toast", null, 0.5f, 1.12f);
        void OnSolved(Vector3 p) { Sfx.Stinger("stinger.solved"); Sfx.Play("sfx.spores", p, 1f); }
        void OnFailed(Vector3 p) => Sfx.Stinger("stinger.fail", 0.8f);

        // ---- sites / obelisk / tornado ---------------------------------------------------------------------
        void OnRepaired(TaskSite s)
        {
            if (s == null) return;
            Sfx.Stinger("stinger.repaired");
            if (s.type == TaskType.PowerPlant) Sfx.Play("sfx.turbine.spinup", s.transform.position + Vector3.up * 3f);
        }

        void OnDepleted(TaskSite s) { if (s != null) Sfx.Play("sfx.site.down", s.transform.position + Vector3.up * 3f); }

        void OnHealthDelta(TaskSite s, float d)
        {
            if (s == null || d <= 0f || !_repairers.TryGetValue(s, out int c) || c == 0) return;
            _nextTick.TryGetValue(s, out float next);
            if (Time.time < next) return;
            _nextTick[s] = Time.time + Random.Range(0.35f, 0.7f);
            Sfx.Play("sfx.repair.tick", s.transform.position + Vector3.up * Random.Range(1f, 4f), 0.7f);
        }

        void OnShieldBuilt(TaskSite s) { Sfx.Play("sfx.shield.build", s != null ? s.transform.position + Vector3.up * 3f : (Vector3?)null); Sfx.Stinger("stinger.shield"); }
        void OnShieldBroken(TaskSite s) => Sfx.Play("sfx.shield.break", s != null ? s.transform.position + Vector3.up * 3f : (Vector3?)null);

        void OnShard(Vector3 p)
        {
            _shardCombo = Time.time < _shardComboUntil ? _shardCombo + 1 : 0;
            _shardComboUntil = Time.time + 3f;
            Sfx.Play("sfx.shard.pickup", p, 1f, _shardCombo >= 3 ? 1.335f : 1f, _shardCombo % 3);
        }

        void OnObelisk(ObeliskStage st, Vector3 p)
        {
            switch (st)
            {
                case ObeliskStage.Exposed: Sfx.Play("sfx.shield.break", p + Vector3.up * 2f); break;
                case ObeliskStage.Cracked: Sfx.Play("sfx.hit", p + Vector3.up * 2f, 1f, 0.8f); break;
                case ObeliskStage.Destroyed: Sfx.Play("sfx.tornado.hit", p, 1f, 0.8f); Sfx.Stinger("stinger.obelisk_down"); break;
            }
        }

        void OnTornado(Vector3 p) { Sfx.Stinger("stinger.danger", 0.9f); _nextGust = Time.time + 1.5f; }
        void OnTornadoHit(Vector3 p) => Sfx.Play("sfx.tornado.hit", p);

        // ---- companion / systems -----------------------------------------------------------------------------
        void OnScanStart()
        {
            Sfx.Play("sfx.scan.start", null, 0.8f);
            if (_scanLoop == null) _scanLoop = Sfx.StartLoop("sfx.scan.loop", null, 0.6f);
        }

        void OnScanStop()
        {
            Sfx.Play("sfx.scan.stop", null, 0.7f);
            Sfx.StopLoop(_scanLoop, 0.15f);
            _scanLoop = null;
        }

        void OnShutdown() { Sfx.Play("sfx.daffodil.shutdown", null, 0.9f); Sfx.StopLoop(_scanLoop, 0.05f); _scanLoop = null; }
        void OnBoot() => Sfx.Play("sfx.daffodil.boot", null, 0.9f);

        void OnLifeLow()
        {
            Sfx.Stinger("stinger.danger", 0.7f);
            _lifeWasLow = true;
            if (_alarmLoop == null) _alarmLoop = Sfx.StartLoop("sfx.ls.alarm", null, 0.8f);
        }

        void OnLifeDepleted() { Sfx.Play("ui.gameover", null, 1f); Sfx.StopLoop(_alarmLoop, 0.2f); _alarmLoop = null; }
        void OnCheckpoint() => Sfx.Play("ui.checkpoint", null, 0.8f);
        void OnToast(string t) => Sfx.Play("ui.toast", null, 0.55f);
        void OnObjective(string t) => Sfx.Play("ui.objective", null, 0.8f);
        void OnLoreOpened(string id) { Sfx.Play("sfx.lore.open", null, 0.9f); Sfx.Stinger("stinger.discovery", 0.6f); }
        void OnLoreClosed(string id) => Sfx.Play("sfx.lore.close", null, 0.8f);
        void OnReport(string grade) => Sfx.Stinger("stinger.ending");
        void OnPaths() => Sfx.Stinger("stinger.paths");
        void OnBunker() => Sfx.Stinger("stinger.discovery");
        void OnGrove(Vector3 p) => Sfx.Play("sfx.spores", p, 0.8f, 0.9f);
        void OnAssessment(string task, int count) => Sfx.Play("ui.objective", null, 0.7f, 1.12f);

        // ---- dialogue blips --------------------------------------------------------------------------------
        void OnDialogueLine(string speaker, string text)
        {
            string s = (speaker ?? "").ToLowerInvariant();
            _blipCue = s.Contains("daffodil") ? "ui.blip.daffodil"
                     : s.Contains("eschrop") || s.Contains("escrop") ? "ui.blip.eschropita" : "ui.blip.mycari";
            _blipChars = string.IsNullOrEmpty(text) ? 0 : text.Length;
            _blipDone = 0;
            _blipNext = Time.unscaledTime + 0.02f;
        }

        void OnDialogueEnded() { _blipCue = null; }

        void Update()
        {
            // Dialogue types ~35 characters per second: one soft blip every ~3 characters while typing.
            if (_blipCue != null)
            {
                var dlg = DialogueSystem.Instance;
                bool typing = dlg != null && dlg.IsTyping;
                if (!typing || _blipDone >= _blipChars) { if (!typing) _blipCue = null; }
                else if (Time.unscaledTime >= _blipNext)
                {
                    Sfx.Play(_blipCue, null, 1f, Random.Range(0.97f, 1.05f));
                    _blipDone += 3;
                    _blipNext = Time.unscaledTime + 3f / 35f * Random.Range(0.85f, 1.2f);
                }
            }

            // Tornado gusts while a twister roams.
            var bridge = SignalBridge.Instance;
            var tornado = bridge != null ? bridge.Tornado : null;
            if (tornado != null && tornado.IsActive && Time.time >= _nextGust)
            {
                _nextGust = Time.time + Random.Range(2.2f, 4.5f);
                Vector2 r = Random.insideUnitCircle * 6f;
                Sfx.Play("sfx.tornado.gust", tornado.Position + new Vector3(r.x, 2f, r.y));
            }

            // Life-support alarm: stops above 35 %; a warm flourish once it climbs back over 60 %.
            var life = LifeSupport.Instance;
            if (life != null)
            {
                float v = life.Value01;
                if (_alarmLoop != null && (v > 0.35f || !life.Active)) { Sfx.StopLoop(_alarmLoop, 0.6f); _alarmLoop = null; }
                if (_lifeWasLow && v > 0.6f) { _lifeWasLow = false; Sfx.Play("sfx.ls.restored", null, 0.9f); }
            }
        }
    }
}
