using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eschropita.Overhaul.EditorTools
{
    /// <summary>
    /// Headless PLAY-MODE screenshots: the game actually runs (runtime systems, atmosphere, VFX, sprites), frames
    /// advance normally (HDRP exposure / TAA / volumetric history converge), and shots are read from the main camera.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod Eschropita.Overhaul.EditorTools.OverhaulPlayCapture.Run
    ///             -shotOut &lt;dir&gt; -shotSpec &lt;json&gt; [-buildLevel] [-overhaulScene &lt;path&gt;]
    ///
    /// Spec (JSON):
    /// { "width":1280, "height":720, "startPhase":"Sandbox", "settleSeconds":6, "timeoutSeconds":600, "noOverhaul":false,
    ///   "shots":[
    ///     { "name":"grove_game", "mode":"game", "teleportPlayer":[14.3,4.0], "wait":2.5 },
    ///     { "name":"turbine", "mode":"camera", "pos":[-6,16,0], "lookAt":[-12.6,12,13.2], "fov":40, "wait":1.0,
    ///       "raise":["SiteDepleted"], "timeScale":1 } ] }
    /// "raise" calls GameSignals.Raise&lt;Name&gt;(...) with defaults (Vector3 → the shot's focus, float → 0.5, bool → true,
    /// int → 0, TaskSite → the windmill, NPCFlocker → nearest Mycari, GamePhase → startPhase, string → "preview").
    /// "game" mode keeps the gameplay camera (Cinemachine) — use teleportPlayer to frame a place as the player sees it.
    /// </summary>
    [InitializeOnLoad]
    public static class OverhaulPlayCapture
    {
        const string JobKey = "OverhaulPlayCapture.Job";

        [Serializable]
        public class Shot
        {
            public string name = "shot";
            public string mode = "camera";
            public float[] pos;
            public float[] lookAt;
            public float fov = 40f;
            public float[] teleportPlayer;
            public float wait = 1.5f;
            public string[] raise;
            public float timeScale = 1f;
            /// <summary>Render Screen Space Overlay canvases into the shot (switched to Screen Space Camera while capturing).</summary>
            public bool includeUI;
        }

        [Serializable]
        public class Spec
        {
            public int width = 1280;
            public int height = 720;
            public string startPhase = "";
            public float settleSeconds = 6f;
            public float timeoutSeconds = 600f;
            public bool noOverhaul = false;
            public Shot[] shots = new Shot[0];
        }

        [Serializable]
        class Job
        {
            public string outDir;
            public string specJson;
        }

        // ---- runtime state (lives across editor updates while playing) ----
        static Job _job;
        static Spec _spec;
        static int _shotIndex = -1;
        static double _phaseUntil;
        static double _startedAt;
        static bool _shotPrepared;
        static RenderTexture _rt;
        static Camera _cam;
        static Behaviour _brain;
        static readonly StringBuilder _report = new StringBuilder();

        static OverhaulPlayCapture()
        {
            string json = SessionState.GetString(JobKey, "");
            if (string.IsNullOrEmpty(json)) return;
            _job = JsonUtility.FromJson<Job>(json);
            _spec = JsonUtility.FromJson<Spec>(_job.specJson) ?? new Spec();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>Batch entry point.</summary>
        public static void Run()
        {
            try
            {
                string outDir = Arg("-shotOut") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp", "PlayShots");
                string specPath = Arg("-shotSpec");
                string scenePath = Arg("-overhaulScene") ?? "Assets/Main Mars Scene.unity";
                string specJson = !string.IsNullOrEmpty(specPath) && File.Exists(specPath) ? File.ReadAllText(specPath) : JsonUtility.ToJson(new Spec());
                var spec = JsonUtility.FromJson<Spec>(specJson) ?? new Spec();
                Directory.CreateDirectory(outDir);

                if (Environment.GetCommandLineArgs().Contains("-buildLevel"))
                {
                    var built = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    string report = OverhaulLevelBuilder.Build(built, save: true);
                    File.WriteAllText(Path.Combine(outDir, "build_report.txt"), report);
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (!string.IsNullOrEmpty(spec.startPhase) && Enum.TryParse(spec.startPhase, out GamePhase phase))
                {
                    var gfm = UnityEngine.Object.FindAnyObjectByType<GameFlowManager>(FindObjectsInactive.Include);
                    if (gfm != null)
                    {
                        var so = new SerializedObject(gfm);
                        var p = so.FindProperty("debugStartPhase");
                        if (p != null) { p.enumValueIndex = (int)phase; so.ApplyModifiedPropertiesWithoutUndo(); }
                    }
                }

                SessionState.SetString(JobKey, JsonUtility.ToJson(new Job { outDir = outDir, specJson = specJson }));
                _job = null; // re-read after the play-mode domain reload
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                _job = new Job { outDir = outDir, specJson = specJson };
                _spec = spec;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[PlayCapture] setup failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static void Tick()
        {
            if (_job == null || _spec == null) return;
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (_startedAt <= 0)
            {
                _startedAt = now;
                _phaseUntil = now + Mathf.Max(0.5f, _spec.settleSeconds);
                if (_spec.noOverhaul) OverhaulBootstrap.Enabled = false;
                _report.AppendLine($"play started; settle {_spec.settleSeconds}s; phase {(GameFlowManager.Instance ? GameFlowManager.Instance.Phase.ToString() : "?")}");
                return;
            }
            if (now - _startedAt > _spec.timeoutSeconds) { Finish(3, "timeout"); return; }
            if (now < _phaseUntil) return;

            try
            {
                if (_shotIndex < 0)
                {
                    SetupCamera();
                    _shotIndex = 0;
                    _shotPrepared = false;
                }
                if (_shotIndex >= (_spec.shots?.Length ?? 0)) { Finish(0, "done"); return; }

                var s = _spec.shots[_shotIndex];
                if (!_shotPrepared)
                {
                    PrepareShot(s);
                    _shotPrepared = true;
                    _phaseUntil = now + Mathf.Max(0.2f, s.wait);
                    return;
                }
                CaptureShot(s);
                _shotIndex++;
                _shotPrepared = false;
                _phaseUntil = now + 0.1f;
            }
            catch (Exception e)
            {
                _report.AppendLine("ERROR " + e);
                Debug.LogException(e);
                _shotIndex++;
                _shotPrepared = false;
            }
        }

        static void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null) _cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            _rt = new RenderTexture(_spec.width, _spec.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _rt.Create();
            _cam.targetTexture = _rt;
            _brain = _cam.GetComponents<Behaviour>().FirstOrDefault(b => b != null && b.GetType().Name == "CinemachineBrain");
        }

        static void PrepareShot(Shot s)
        {
            Time.timeScale = s.timeScale > 0f ? s.timeScale : 1f;
            Vector3 focus = Vector3.zero;
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();

            if (s.teleportPlayer != null && s.teleportPlayer.Length >= 2 && player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
                float x = s.teleportPlayer[0], z = s.teleportPlayer[1];
                var t = Terrain.activeTerrain;
                float y = t ? t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y + 1.0f : player.transform.position.y;
                player.transform.position = new Vector3(x, y, z);
                if (cc) cc.enabled = true;
            }

            if (s.mode == "camera" && s.pos != null && s.pos.Length >= 3 && s.lookAt != null && s.lookAt.Length >= 3)
            {
                if (_brain) _brain.enabled = false;
                Vector3 p = new Vector3(s.pos[0], s.pos[1], s.pos[2]);
                focus = new Vector3(s.lookAt[0], s.lookAt[1], s.lookAt[2]);
                _cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(focus - p, Vector3.up));
                _cam.fieldOfView = s.fov;
            }
            else
            {
                if (_brain) _brain.enabled = true;
                focus = player ? player.transform.position : Vector3.zero;
            }

            if (s.raise != null)
                foreach (var r in s.raise) RaiseSignal(r, focus);
            SetUiCapture(s.includeUI);
        }

        static readonly List<Canvas> _uiSwitched = new List<Canvas>();

        /// <summary>Overlay canvases never reach a camera's target texture; move them in front of the camera.</summary>
        static void SetUiCapture(bool on)
        {
            if (!on)
            {
                foreach (var c in _uiSwitched) if (c) { c.renderMode = RenderMode.ScreenSpaceOverlay; }
                _uiSwitched.Clear();
                return;
            }
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = _cam;
                c.planeDistance = Mathf.Max(_cam.nearClipPlane * 2f, 0.35f);
                _uiSwitched.Add(c);
            }
        }

        static void RaiseSignal(string name, Vector3 focus)
        {
            var m = typeof(GameSignals).GetMethod("Raise" + name, BindingFlags.Public | BindingFlags.Static);
            if (m == null) { _report.AppendLine("unknown signal " + name); return; }
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type t = ps[i].ParameterType;
                if (t == typeof(Vector3)) args[i] = focus;
                else if (t == typeof(float)) args[i] = 0.5f;
                else if (t == typeof(bool)) args[i] = true;
                else if (t == typeof(int)) args[i] = 0;
                else if (t == typeof(string)) args[i] = "preview";
                else if (t == typeof(TaskSite)) args[i] = SignalBridge.Instance ? SignalBridge.Instance.Windmill : UnityEngine.Object.FindAnyObjectByType<TaskSite>();
                else if (t == typeof(NPCFlocker)) args[i] = UnityEngine.Object.FindAnyObjectByType<NPCFlocker>();
                else if (t == typeof(GamePhase)) args[i] = GameFlowManager.Instance ? GameFlowManager.Instance.Phase : GamePhase.Sandbox;
                else if (t.IsEnum) args[i] = Enum.GetValues(t).GetValue(0);
                else args[i] = null;
            }
            m.Invoke(null, args);
            _report.AppendLine($"raised {name}");
        }

        static void CaptureShot(Shot s)
        {
            var tex = new Texture2D(_spec.width, _spec.height, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            tex.ReadPixels(new Rect(0, 0, _spec.width, _spec.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string file = Path.Combine(_job.outDir, Sanitize(s.name) + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            string phase = GameFlowManager.Instance ? GameFlowManager.Instance.Phase.ToString() : "?";
            _report.AppendLine($"shot {s.name} t={Time.timeSinceLevelLoad:0.0}s phase={phase} cam={_cam.transform.position} -> {file}");
        }

        static void Finish(int code, string why)
        {
            _report.AppendLine("finish: " + why);
            try
            {
                if (_job != null) File.WriteAllText(Path.Combine(_job.outDir, "play_capture_report.txt"), _report.ToString());
            }
            catch { }
            SessionState.EraseString(JobKey);
            _job = null;
            EditorApplication.update -= Tick;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "shot") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }
}
