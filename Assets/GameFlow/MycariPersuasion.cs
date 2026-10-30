using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The step between asking a Mycari and giving it orders (sandbox):
///  1. Ask (E): it agrees to hear you out and walks to the mushroom grove.
///  2. Meet it there: it sings you its melody, walking the mushrooms.
///  3. Sing it back. Right: it listens, follows you and takes any order. A wrong note: it loses interest, turns
///     red and wanders home, and won't hear you out again for a little while.
/// A Mycari you have won over remembers your song: ask it again later and it follows straight away.
/// One song at a time. Melodies grow with each Mycari won over (Breakers are harder to impress) and ease off
/// by a note after a miss. The scripted tutorial (before the sandbox) is untouched.
/// </summary>
[DisallowMultipleComponent]
public class MycariPersuasion : MonoBehaviour
{
    public static MycariPersuasion Instance { get; private set; }

    [Header("Melody length")]
    [Min(1)] public int baseLength = 4;
    [Tooltip("Extra notes for a Breaker.")]
    [Min(0)] public int breakerExtra = 1;
    [Tooltip("One more note after this many Mycari won over, up to maxExtra.")]
    [Min(1)] public int winsPerStep = 2;
    [Min(0)] public int maxExtra = 2;

    [Header("Timing")]
    [Tooltip("How long the Mycari waits at the grove for you before wandering home.")]
    [Min(5f)] public float waitForPlayerSeconds = 75f;
    [Tooltip("After a missed song it won't hear you out again for this long.")]
    [Min(0f)] public float refuseSeconds = 20f;
    [Tooltip("Walking this far from the grove while the song is on abandons it.")]
    [Min(5f)] public float abandonDistance = 16f;

    [Header("Memory")]
    [Tooltip("A Mycari won over once follows straight away when asked again.")]
    public bool rememberSong = true;

    /// <summary>True in the sandbox (after the tutorial), when the grove and its mushrooms are there.</summary>
    public bool Active
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            Resolve();
            if (_puzzle == null || _grove == null) return false;
            var flow = GameFlowManager.Instance;
            return flow == null || flow.SandboxUnlocked;
        }
    }

    /// <summary>A Mycari is on its way to the grove or singing with you.</summary>
    public bool Busy => _candidate != null;
    public NPCFlocker Candidate => _candidate;

    public bool Remembers(NPCFlocker n) => rememberSong && n != null && _won.Contains(n);

    /// <summary>Counts <paramref name="n"/> as won over (the tutorial's Mycari heard your first song).</summary>
    public void Remember(NPCFlocker n) { if (n != null) _won.Add(n); }

    readonly HashSet<NPCFlocker> _won = new HashSet<NPCFlocker>();
    readonly Dictionary<NPCFlocker, float> _refuseUntil = new Dictionary<NPCFlocker, float>();
    NPCFlocker _candidate;
    Coroutine _session;
    MartianSequencePuzzle _puzzle;
    MushroomFieldTrigger _grove;
    PlayerController _player;
    int _wins, _missStreak;
    bool _solved, _missed, _hinted;
    float _nextResolve;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unhook();
    }

    void Resolve()
    {
        if (_puzzle != null && _grove != null && _player != null) return;
        if (Time.unscaledTime < _nextResolve) return;   // missing pieces: look again now and then, not every frame
        _nextResolve = Time.unscaledTime + 2f;
        if (_puzzle == null)
        {
            _puzzle = FindAnyObjectByType<MartianSequencePuzzle>();
            if (_puzzle != null)
            {
                _puzzle.OnSuccess += OnSongSolved;
                _puzzle.OnFail += OnSongMissed;
            }
        }
        if (_grove == null) _grove = FindAnyObjectByType<MushroomFieldTrigger>();
        if (_player == null) _player = FindAnyObjectByType<PlayerController>();
    }

    void Unhook()
    {
        if (_puzzle == null) return;
        _puzzle.OnSuccess -= OnSongSolved;
        _puzzle.OnFail -= OnSongMissed;
    }

    void OnSongSolved() { if (Busy) _solved = true; }
    void OnSongMissed() { if (Busy) _missed = true; }

    void Update()
    {
        if (!_hinted && Active)
        {
            _hinted = true;
            Toast("Daffodil: The Mycari only take orders once they've heard your song at the grove.", 4.5f);
        }
        // Game over / checkpoint rollback: the song is off.
        var flow = GameFlowManager.Instance;
        if (Busy && flow != null && !flow.PlayerControlEnabled) Cancel(false, null);
    }

    // ------------------------------------------------------------------ asking

    /// <summary>
    /// The player asked <paramref name="n"/> (E). Returns true when it is now walking to the grove (or, having
    /// heard your song before, follows you at once). <paramref name="message"/> is a line for the HUD either way.
    /// </summary>
    public bool TryAsk(NPCFlocker n, PlayerController player, out string message)
    {
        message = null;
        if (n == null || player == null || !Active) return false;
        _player = player;

        if (Remembers(n))
        {
            n.ListenTo(player.transform);
            player.AdoptFollower(n);
            message = "It remembers your song and follows you.";
            Eschropita.Overhaul.GameSignals.RaiseMycariPersuaded(n);
            return true;
        }
        if (_refuseUntil.TryGetValue(n, out float until) && Time.time < until)
        {
            message = "It isn't ready to hear you out again yet.";
            return false;
        }
        if (Busy)
        {
            message = "One song at a time: a Mycari is already waiting at the grove.";
            return false;
        }

        _candidate = n;
        _solved = _missed = false;
        _session = StartCoroutine(Session(n));
        message = "It will hear your song. Meet it at the mushroom grove.";
        Eschropita.Overhaul.GameSignals.RaiseMycariAsked(n);
        return true;
    }

    // ------------------------------------------------------------------ the song

    IEnumerator Session(NPCFlocker n)
    {
        SetObjective("Meet the Mycari at the mushroom grove");

        // 1) It walks to the grove. A Mycari that gets stuck on the way is put at the grove's edge so the song
        //    can still happen (ScriptedMoveTo reports arrival after a timeout even when it never got there).
        Vector3 spot = WaitingSpot();
        bool there = false;
        n.ScriptedMoveTo(spot, 1.6f, () => there = true);
        while (!there)
        {
            if (n == null) { End(); yield break; }
            yield return null;
        }
        if (Flat(n.transform.position - spot) > 6f) PlaceAt(n, spot);

        // 2) It waits for you.
        float deadline = Time.time + waitForPlayerSeconds;
        while (!PlayerAtGrove())
        {
            if (n == null) { End(); yield break; }
            if (Time.time > deadline)
            {
                Cancel(false, "The Mycari got tired of waiting and wandered home.");
                yield break;
            }
            yield return null;
        }

        // 3) Its melody: it walks the mushrooms.
        int len = MelodyLength(n);
        _puzzle.ResetPuzzle();
        _puzzle.SetSequenceLength(len, len);
        _puzzle.replayAfterWrong = false;   // one go
        SetObjective($"Watch its melody ({len} notes)");
        bool demoDone = false;
        _puzzle.StartDemo(n, () => demoDone = true);
        while (!demoDone)
        {
            if (n == null || Abandoned()) { Cancel(true, "You walked away from its song."); yield break; }
            yield return null;
        }

        // 4) Your turn.
        _solved = _missed = false;
        SetObjective("Sing it back: walk up to each mushroom and press E");
        _puzzle.BeginPlayerAttempt();
        while (!_solved && !_missed)
        {
            if (n == null || Abandoned()) { Cancel(true, "You walked away from its song."); yield break; }
            yield return null;
        }

        if (_solved)
        {
            _wins++;
            _missStreak = 0;
            _won.Add(n);
            _puzzle.replayAfterWrong = true;
            if (_player != null)
            {
                n.ListenTo(_player.transform);
                _player.AdoptFollower(n);
            }
            Toast("It listens! Lead it to a site and press E to give orders.", 3.5f);
            Eschropita.Overhaul.GameSignals.RaiseMycariPersuaded(n);
            End();
            yield break;
        }

        // A wrong note: let the red feedback play out, then it leaves.
        _missStreak++;
        yield return new WaitForSeconds(1.6f);
        _refuseUntil[n] = Time.time + refuseSeconds;
        Cancel(true, "A wrong note. It loses interest and wanders home.");
    }

    /// <summary>Ends the session: the pads go idle, the Mycari (if any) turns away home.</summary>
    void Cancel(bool walkOff, string line)
    {
        var n = _candidate;
        if (_session != null) StopCoroutine(_session);
        if (_puzzle != null)
        {
            _puzzle.ResetPuzzle();
            _puzzle.replayAfterWrong = true;
        }
        if (n != null)
        {
            if (walkOff) n.WalkOff();
            else n.ScriptedRelease();
        }
        if (!string.IsNullOrEmpty(line)) Toast(line, 3f);
        End();
    }

    void End()
    {
        _session = null;
        _candidate = null;
        _solved = _missed = false;
        SetObjective(null);
    }

    int MelodyLength(NPCFlocker n)
    {
        int len = baseLength + (n.role == NPCRole.Attacker ? breakerExtra : 0)
                + Mathf.Min(maxExtra, _wins / Mathf.Max(1, winsPerStep))
                - (_missStreak > 0 ? 1 : 0);
        return Mathf.Clamp(len, 3, 8);
    }

    // ------------------------------------------------------------------ places

    Vector3 WaitingSpot()
    {
        Vector3 c = _grove.Center;
        if (NavMesh.SamplePosition(c, out var hit, 4f, NavMesh.AllAreas)) c = hit.position;
        return c;
    }

    bool PlayerAtGrove()
    {
        if (_player == null) return false;
        return _grove.PlayerInside || Flat(_player.transform.position - _grove.Center) < 7f;
    }

    bool Abandoned() => _player != null && Flat(_player.transform.position - _grove.Center) > abandonDistance;

    static void PlaceAt(NPCFlocker n, Vector3 spot)
    {
        var agent = n.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh && NavMesh.SamplePosition(spot, out var hit, 4f, NavMesh.AllAreas)) agent.Warp(hit.position);
        else n.transform.position = spot;
    }

    static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

    static void SetObjective(string text)
    {
        var flow = GameFlowManager.Instance;
        if (flow != null) flow.OverrideObjective(text);
        else if (GameHUD.Instance != null && text != null) GameHUD.Instance.SetObjective(text);
    }

    static void Toast(string text, float seconds)
    {
        if (GameHUD.Instance != null) GameHUD.Instance.Toast(text, seconds);
        else Debug.Log("[Mycari] " + text);
    }
}
