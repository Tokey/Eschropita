using System;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// One place where gameplay code announces "something happened" so the overhaul layers
    /// (music, SFX, VFX, camera, HUD juice, report card) can react without the gameplay code knowing
    /// they exist. Gameplay calls the Raise methods (one line each); listeners subscribe to the events.
    /// Every Raise is safe to call with nobody listening.
    ///
    /// Events that already exist on gameplay objects (puzzle, pads' sites, obelisk, tornado, life support,
    /// Daffodil, dialogue, checkpoints) are bridged in by <see cref="SignalBridge"/>, so most code never needs editing.
    /// </summary>
    public static class GameSignals
    {
        // ---- player -------------------------------------------------------------------------------------------
        /// <summary>A footstep landed. Position is at the feet; speed01 is 0 (walk) .. 1 (run).</summary>
        public static event Action<Vector3, float> Footstep;
        /// <summary>The player pressed interact and something accepted it (pad, site, NPC, lore).</summary>
        public static event Action<Vector3> Interacted;
        /// <summary>The player pressed interact and nothing was in range.</summary>
        public static event Action<Vector3> InteractNothing;

        // ---- Mycari -------------------------------------------------------------------------------------------
        /// <summary>A Mycari agreed to follow the player (recruited).</summary>
        public static event Action<NPCFlocker> MycariRecruited;
        /// <summary>A Mycari refused to follow / refused a command.</summary>
        public static event Action<NPCFlocker> MycariDeclined;
        /// <summary>The player dismissed the follower.</summary>
        public static event Action<NPCFlocker> MycariDismissed;
        /// <summary>The player gave a command (repair / dismantle) at a site.</summary>
        public static event Action<NPCFlocker, TaskSite, TaskCommand> MycariCommanded;
        /// <summary>A Mycari reached its work site and started working.</summary>
        public static event Action<NPCFlocker, TaskSite> MycariStartedWork;
        /// <summary>A Mycari finished (or abandoned) work at a site.</summary>
        public static event Action<NPCFlocker, TaskSite> MycariFinishedWork;
        /// <summary>An attacking Mycari landed a hit (burst frame) on a site.</summary>
        public static event Action<NPCFlocker, Vector3> MycariStrike;
        /// <summary>A Mycari left through the dome door / went home.</summary>
        public static event Action<NPCFlocker, Vector3> MycariLeftHome;

        // ---- puzzle -------------------------------------------------------------------------------------------
        /// <summary>A mushroom pad sounded during the demo / sequence playback. (padIndex, pad world position)</summary>
        public static event Action<int, Vector3> PadSang;
        /// <summary>The player pressed a pad. (padIndex, position, correct)</summary>
        public static event Action<int, Vector3, bool> PadPressed;
        /// <summary>The player's turn in the call-and-response started.</summary>
        public static event Action PuzzlePlayerTurn;
        /// <summary>The whole melody was answered correctly. Position = grove centre.</summary>
        public static event Action<Vector3> PuzzleSolved;
        /// <summary>The answer was wrong; the mushrooms will sing again. Position = grove centre.</summary>
        public static event Action<Vector3> PuzzleFailed;

        // ---- sites / obelisk / tornado ---------------------------------------------------------------------
        public static event Action<TaskSite> SiteRepaired;
        public static event Action<TaskSite> SiteDepleted;
        public static event Action<TaskSite, float> SiteHealthDelta;
        public static event Action<TaskSite> SiteShieldBuilt;
        public static event Action<TaskSite> SiteShieldBroken;
        public static event Action<Vector3> ShardCollected;
        public static event Action<ObeliskStage, Vector3> ObeliskStageChanged;
        public static event Action<Vector3> TornadoSpawned;
        public static event Action<Vector3> TornadoEnded;
        public static event Action<Vector3> TornadoHit;

        // ---- companion / systems --------------------------------------------------------------------------
        public static event Action ScanStarted;
        public static event Action ScanStopped;
        public static event Action DaffodilShutdown;
        public static event Action DaffodilBoot;
        public static event Action<float> LifeSupportChanged;
        public static event Action LifeSupportLow;
        public static event Action LifeSupportDepleted;
        public static event Action CheckpointSaved;
        public static event Action CheckpointRestored;
        public static event Action<GamePhase> PhaseChanged;
        public static event Action<string, string> DialogueLine;   // speaker, text
        public static event Action DialogueEnded;
        public static event Action<string> ToastShown;
        public static event Action<string> ObjectiveChanged;
        public static event Action BunkerEntered;
        public static event Action<Vector3> GroveEntered;

        // ---- lore / endings (new systems) ------------------------------------------------------------------
        public static event Action<string> LoreOpened;   // lore id
        public static event Action<string> LoreClosed;
        public static event Action<string> ReportCardShown; // grade id: "excellent" | "moderate" | "poor"
        public static event Action LightPathsRevealed;

        // ---- spore attunement / assessment (overhaul mechanics) -------------------------------------------
        /// <summary>The player lingered near a Mycari long enough (spore attunement) to perceive its true role.</summary>
        public static event Action<NPCFlocker> MycariAttuned;
        /// <summary>The player asked a Mycari to hear their song; it is walking to the grove.</summary>
        public static event Action<NPCFlocker> MycariAsked;
        /// <summary>A Mycari heard the player's song (or remembered it) and now follows, taking orders.</summary>
        public static event Action<NPCFlocker> MycariPersuaded;
        /// <summary>A Field Assessment task was completed. (task id: "repair" | "dismantle" | "freedom", new count)</summary>
        public static event Action<string, int> AssessmentProgress;

        // ---- raise helpers (null-safe, exception-isolated so a broken listener never breaks gameplay) ----------
        static void Safe(Action a) { if (a == null) return; foreach (Action d in a.GetInvocationList()) { try { d(); } catch (Exception e) { Debug.LogException(e); } } }
        static void Safe<T>(Action<T> a, T x) { if (a == null) return; foreach (Action<T> d in a.GetInvocationList()) { try { d(x); } catch (Exception e) { Debug.LogException(e); } } }
        static void Safe<T, U>(Action<T, U> a, T x, U y) { if (a == null) return; foreach (Action<T, U> d in a.GetInvocationList()) { try { d(x, y); } catch (Exception e) { Debug.LogException(e); } } }
        static void Safe<T, U, V>(Action<T, U, V> a, T x, U y, V z) { if (a == null) return; foreach (Action<T, U, V> d in a.GetInvocationList()) { try { d(x, y, z); } catch (Exception e) { Debug.LogException(e); } } }

        public static void RaiseFootstep(Vector3 p, float speed01) => Safe(Footstep, p, speed01);
        public static void RaiseInteracted(Vector3 p) => Safe(Interacted, p);
        public static void RaiseInteractNothing(Vector3 p) => Safe(InteractNothing, p);
        public static void RaiseMycariRecruited(NPCFlocker n) => Safe(MycariRecruited, n);
        public static void RaiseMycariDeclined(NPCFlocker n) => Safe(MycariDeclined, n);
        public static void RaiseMycariDismissed(NPCFlocker n) => Safe(MycariDismissed, n);
        public static void RaiseMycariCommanded(NPCFlocker n, TaskSite s, TaskCommand c) => Safe(MycariCommanded, n, s, c);
        public static void RaiseMycariStartedWork(NPCFlocker n, TaskSite s) => Safe(MycariStartedWork, n, s);
        public static void RaiseMycariFinishedWork(NPCFlocker n, TaskSite s) => Safe(MycariFinishedWork, n, s);
        public static void RaiseMycariStrike(NPCFlocker n, Vector3 p) => Safe(MycariStrike, n, p);
        public static void RaiseMycariLeftHome(NPCFlocker n, Vector3 p) => Safe(MycariLeftHome, n, p);
        public static void RaisePadSang(int i, Vector3 p) => Safe(PadSang, i, p);
        public static void RaisePadPressed(int i, Vector3 p, bool ok) => Safe(PadPressed, i, p, ok);
        public static void RaisePuzzlePlayerTurn() => Safe(PuzzlePlayerTurn);
        public static void RaisePuzzleSolved(Vector3 p) => Safe(PuzzleSolved, p);
        public static void RaisePuzzleFailed(Vector3 p) => Safe(PuzzleFailed, p);
        public static void RaiseSiteRepaired(TaskSite s) => Safe(SiteRepaired, s);
        public static void RaiseSiteDepleted(TaskSite s) => Safe(SiteDepleted, s);
        public static void RaiseSiteHealthDelta(TaskSite s, float d) => Safe(SiteHealthDelta, s, d);
        public static void RaiseSiteShieldBuilt(TaskSite s) => Safe(SiteShieldBuilt, s);
        public static void RaiseSiteShieldBroken(TaskSite s) => Safe(SiteShieldBroken, s);
        public static void RaiseShardCollected(Vector3 p) => Safe(ShardCollected, p);
        public static void RaiseObeliskStageChanged(ObeliskStage st, Vector3 p) => Safe(ObeliskStageChanged, st, p);
        public static void RaiseTornadoSpawned(Vector3 p) => Safe(TornadoSpawned, p);
        public static void RaiseTornadoEnded(Vector3 p) => Safe(TornadoEnded, p);
        public static void RaiseTornadoHit(Vector3 p) => Safe(TornadoHit, p);
        public static void RaiseScanStarted() => Safe(ScanStarted);
        public static void RaiseScanStopped() => Safe(ScanStopped);
        public static void RaiseDaffodilShutdown() => Safe(DaffodilShutdown);
        public static void RaiseDaffodilBoot() => Safe(DaffodilBoot);
        public static void RaiseLifeSupportChanged(float v01) => Safe(LifeSupportChanged, v01);
        public static void RaiseLifeSupportLow() => Safe(LifeSupportLow);
        public static void RaiseLifeSupportDepleted() => Safe(LifeSupportDepleted);
        public static void RaiseCheckpointSaved() => Safe(CheckpointSaved);
        public static void RaiseCheckpointRestored() => Safe(CheckpointRestored);
        public static void RaisePhaseChanged(GamePhase p) => Safe(PhaseChanged, p);
        public static void RaiseDialogueLine(string speaker, string text) => Safe(DialogueLine, speaker, text);
        public static void RaiseDialogueEnded() => Safe(DialogueEnded);
        public static void RaiseToastShown(string t) => Safe(ToastShown, t);
        public static void RaiseObjectiveChanged(string t) => Safe(ObjectiveChanged, t);
        public static void RaiseBunkerEntered() => Safe(BunkerEntered);
        public static void RaiseGroveEntered(Vector3 p) => Safe(GroveEntered, p);
        public static void RaiseLoreOpened(string id) => Safe(LoreOpened, id);
        public static void RaiseLoreClosed(string id) => Safe(LoreClosed, id);
        public static void RaiseReportCardShown(string grade) => Safe(ReportCardShown, grade);
        public static void RaiseLightPathsRevealed() => Safe(LightPathsRevealed);
        public static void RaiseMycariAttuned(NPCFlocker n) => Safe(MycariAttuned, n);
        public static void RaiseMycariAsked(NPCFlocker n) => Safe(MycariAsked, n);
        public static void RaiseMycariPersuaded(NPCFlocker n) => Safe(MycariPersuaded, n);
        public static void RaiseAssessmentProgress(string task, int count) => Safe(AssessmentProgress, task, count);

        /// <summary>Clears every subscriber. Called on scene load so stale listeners from a previous scene vanish.</summary>
        public static void ClearAll()
        {
            Footstep = null; Interacted = null; InteractNothing = null;
            MycariRecruited = null; MycariDeclined = null; MycariDismissed = null; MycariCommanded = null;
            MycariStartedWork = null; MycariFinishedWork = null; MycariStrike = null; MycariLeftHome = null;
            PadSang = null; PadPressed = null; PuzzlePlayerTurn = null; PuzzleSolved = null; PuzzleFailed = null;
            SiteRepaired = null; SiteDepleted = null; SiteHealthDelta = null; SiteShieldBuilt = null; SiteShieldBroken = null;
            ShardCollected = null; ObeliskStageChanged = null; TornadoSpawned = null; TornadoEnded = null; TornadoHit = null;
            ScanStarted = null; ScanStopped = null; DaffodilShutdown = null; DaffodilBoot = null;
            LifeSupportChanged = null; LifeSupportLow = null; LifeSupportDepleted = null;
            CheckpointSaved = null; CheckpointRestored = null; PhaseChanged = null; DialogueLine = null; DialogueEnded = null;
            ToastShown = null; ObjectiveChanged = null; BunkerEntered = null; GroveEntered = null;
            LoreOpened = null; LoreClosed = null; ReportCardShown = null; LightPathsRevealed = null;
            MycariAttuned = null; MycariAsked = null; MycariPersuaded = null; AssessmentProgress = null;
        }
    }
}
