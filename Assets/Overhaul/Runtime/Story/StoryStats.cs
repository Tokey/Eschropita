using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// What the Mycari students are quietly grading (design doc: the visit is a civic class project). Tracks the
    /// Field Assessment — the doc's win condition, each task four times:
    ///   Repair (Fixers repair life-sustaining tech) · Dismantling (Breakers take down colonist artefacts) ·
    ///   Freedom (Wanderers you escort leave through the dome door)
    /// plus attunement (songs, perceived Mycari), curiosity (logs, scans), and how close life support came to zero.
    /// Raises <see cref="GameSignals.AssessmentProgress"/> whenever a task count goes up.
    /// </summary>
    [OverhaulModule(6)]
    public class StoryStats : MonoBehaviour
    {
        public static StoryStats Instance { get; private set; }
        public const int TaskGoal = 4;

        public int Repairs, Dismantles, Freedom;
        public int PuzzlesSolved, PuzzlesFailed, Attuned, Scans, Shards, Rollbacks;
        public float ScanSeconds, PlayTime, LowestLife = 1f;
        public readonly HashSet<string> LoreRead = new HashSet<string>();
        readonly int[] _recruits = new int[3];
        readonly Dictionary<NPCFlocker, TaskCommand> _jobCmd = new Dictionary<NPCFlocker, TaskCommand>();
        readonly Dictionary<NPCFlocker, float> _jobStart = new Dictionary<NPCFlocker, float>();
        readonly HashSet<NPCFlocker> _escorted = new HashSet<NPCFlocker>();
        bool _obeliskCounted;
        float _scanStart = -1f;

        public enum Grade { Excellent, Moderate, Poor }

        public int Recruits(NPCRole r) => _recruits[Mathf.Clamp((int)r, 0, 2)];
        public int TotalTasks => Repairs + Dismantles + Freedom;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            GameSignals.MycariRecruited += OnRecruited;
            GameSignals.MycariStartedWork += OnStarted;
            GameSignals.MycariFinishedWork += OnFinished;
            GameSignals.MycariLeftHome += OnLeftHome;
            GameSignals.MycariAttuned += OnAttuned;
            GameSignals.ObeliskStageChanged += OnObelisk;
            GameSignals.PuzzleSolved += OnSolved;
            GameSignals.PuzzleFailed += OnFailed;
            GameSignals.ScanStarted += OnScanStart;
            GameSignals.ScanStopped += OnScanStop;
            GameSignals.ShardCollected += OnShard;
            GameSignals.CheckpointRestored += OnRollback;
            GameSignals.LoreOpened += OnLore;
        }

        void OnDisable()
        {
            GameSignals.MycariRecruited -= OnRecruited;
            GameSignals.MycariStartedWork -= OnStarted;
            GameSignals.MycariFinishedWork -= OnFinished;
            GameSignals.MycariLeftHome -= OnLeftHome;
            GameSignals.MycariAttuned -= OnAttuned;
            GameSignals.ObeliskStageChanged -= OnObelisk;
            GameSignals.PuzzleSolved -= OnSolved;
            GameSignals.PuzzleFailed -= OnFailed;
            GameSignals.ScanStarted -= OnScanStart;
            GameSignals.ScanStopped -= OnScanStop;
            GameSignals.ShardCollected -= OnShard;
            GameSignals.CheckpointRestored -= OnRollback;
            GameSignals.LoreOpened -= OnLore;
        }

        void OnRecruited(NPCFlocker n)
        {
            if (n == null) return;
            _recruits[Mathf.Clamp((int)n.role, 0, 2)]++;
            _escorted.Add(n);
        }

        void OnStarted(NPCFlocker n, TaskSite s)
        {
            if (n == null) return;
            _jobCmd[n] = n.CurrentCommand;
            _jobStart[n] = Time.time;
        }

        void OnFinished(NPCFlocker n, TaskSite s)
        {
            if (n == null || !_jobCmd.TryGetValue(n, out var cmd)) return;
            float worked = Time.time - _jobStart[n];
            _jobCmd.Remove(n);
            _jobStart.Remove(n);
            if (worked < 1.5f) return;
            if (cmd == TaskCommand.Repair) Bump(ref Repairs, "repair");
            else Bump(ref Dismantles, "dismantle");
        }

        void OnLeftHome(NPCFlocker n, Vector3 p)
        {
            // Only Wanderers the player actually escorted count (they also wander off on their own).
            if (n != null && n.role == NPCRole.Wanderer && _escorted.Contains(n)) Bump(ref Freedom, "freedom");
            if (n != null) _escorted.Remove(n);
        }

        void OnObelisk(ObeliskStage st, Vector3 p)
        {
            if (st == ObeliskStage.Destroyed && !_obeliskCounted) { _obeliskCounted = true; Bump(ref Dismantles, "dismantle"); }
        }

        void OnAttuned(NPCFlocker n) => Attuned++;
        void OnSolved(Vector3 p) => PuzzlesSolved++;
        void OnFailed(Vector3 p) => PuzzlesFailed++;
        void OnScanStart() { Scans++; _scanStart = Time.time; }
        void OnScanStop() { if (_scanStart >= 0f) ScanSeconds += Time.time - _scanStart; _scanStart = -1f; }
        void OnShard(Vector3 p) => Shards++;
        void OnRollback() => Rollbacks++;
        void OnLore(string id) { if (!string.IsNullOrEmpty(id)) LoreRead.Add(id); }

        void Bump(ref int counter, string task)
        {
            counter++;
            GameSignals.RaiseAssessmentProgress(task, counter);
        }

        void Update()
        {
            var gfm = GameFlowManager.Instance;
            if (gfm != null && gfm.Phase != GamePhase.IntroVideo && gfm.Phase < GamePhase.FinalReport) PlayTime += Time.deltaTime;
            var life = LifeSupport.Instance;
            if (life != null && life.Active) LowestLife = Mathf.Min(LowestLife, life.Value01);
        }

        // ---- grading (design doc endings) -------------------------------------------------------------------

        /// <summary>Roles the player genuinely worked with: recruited at least once and completed their task.</summary>
        public int GroupsEngaged
        {
            get
            {
                int g = 0;
                if (Repairs > 0) g++;
                if (Dismantles > 0) g++;
                if (Freedom > 0) g++;
                return g;
            }
        }

        /// <summary>
        /// Excellent: worked with all three groups (each task at least twice, or the full assessment).
        /// Moderate: worked with some groups. Poor: neglected multiple groups.
        /// </summary>
        public Grade Overall()
        {
            bool full = Repairs >= TaskGoal && Dismantles >= TaskGoal && Freedom >= TaskGoal;
            if (full || (GroupsEngaged == 3 && Repairs >= 2 && Dismantles >= 2 && Freedom >= 2)) return Grade.Excellent;
            if (GroupsEngaged >= 2 || TotalTasks >= 3) return Grade.Moderate;
            return Grade.Poor;
        }

        public static float Score(int count) => Mathf.Clamp01(count / (float)TaskGoal);

        public static string Letter(float score01)
        {
            if (score01 >= 0.9f) return "A";
            if (score01 >= 0.65f) return "B";
            if (score01 >= 0.35f) return "C";
            return "D";
        }
    }
}
