#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// TEMPORARY diagnostic - delete once it has done its job.
///
/// Play mode floods the console with "Invalid worldAABB / Invalid localAABB. Object transform is corrupt"
/// and IsFinite(distanceForSort) assertions, and Unity's log does not say which object. This checks every
/// enabled renderer four times a second while playing and logs each offender once, with its hierarchy path,
/// so the culprit shows up in Logs/Editor.log (click the entry in the Console to select the object).
/// </summary>
public class NaNRendererProbe : MonoBehaviour
{
    readonly HashSet<Renderer> _reported = new();
    float _nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("NaNRendererProbe") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<NaNRendererProbe>();
        Debug.Log("[NaNRendererProbe] watching renderers for corrupt transforms and bounds.");
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.25f;

        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!r || !r.enabled || _reported.Contains(r)) continue;

            Transform t = r.transform;
            Matrix4x4 m = t.localToWorldMatrix;
            bool badMatrix = !Finite(m) || Mathf.Abs(m.determinant) < 1e-12f;
            Bounds b = r.bounds;
            bool badBounds = !Finite(b.center) || !Finite(b.extents) || b.extents.sqrMagnitude > 1e12f;
            if (!badMatrix && !badBounds) continue;

            _reported.Add(r);
            Debug.LogError($"[NaNRendererProbe] frame {Time.frameCount}: {r.GetType().Name} '{PathOf(t)}' " +
                           $"badMatrix={badMatrix} (det {m.determinant}) badBounds={badBounds} " +
                           $"pos={t.position} localScale={t.localScale} lossyScale={t.lossyScale} bounds={b} " +
                           $"material={(r.sharedMaterial ? r.sharedMaterial.name : "none")}", r);
        }
    }

    static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

    static bool Finite(Matrix4x4 m)
    {
        for (int i = 0; i < 16; i++)
            if (!Finite(m[i])) return false;
        return true;
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
#endif
