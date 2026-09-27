using System;
using System.Collections.Generic;
using UnityEngine;

public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }
    private readonly List<TaskSite> _sites = new List<TaskSite>();
    public IReadOnlyList<TaskSite> Sites => _sites;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Scene scan: sites whose OnEnable ran before this manager existed could not self-register.
        // Register() dedupes, so sites that did register (or will) are not listed twice.
        _sites.Clear();
        foreach (var site in FindObjectsByType<TaskSite>(FindObjectsSortMode.None))
            Register(site);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // === Registry (TaskSite calls these from OnEnable / OnDisable) ===
    public void Register(TaskSite site)
    {
        if (site == null || _sites.Contains(site)) return;
        _sites.Add(site);
    }

    public void Unregister(TaskSite site)
    {
        if (site == null) return;
        _sites.Remove(site);
    }

    // === Queries ===

    /// <summary>First registered site of the given type, or null.</summary>
    public TaskSite FindByType(TaskType t)
    {
        foreach (var s in _sites)
            if (s != null && s.type == t) return s;
        return null;
    }

    /// <summary>
    /// Nearest site (by centre distance) within maxDistance of pos that passes the optional filter.
    /// maxDistance &lt;= 0 means unlimited.
    /// </summary>
    public TaskSite FindNearestSite(Vector3 pos, float maxDistance, Func<TaskSite, bool> filter = null)
    {
        float bestSq = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
        TaskSite best = null;
        foreach (var s in _sites)
        {
            if (s == null) continue;
            if (filter != null && !filter(s)) continue;
            float d = (s.transform.position - pos).sqrMagnitude;
            if (d <= bestSq) { bestSq = d; best = s; }
        }
        return best;
    }

    /// <summary>Legacy overload: the nearest-site fallback has no distance limit.</summary>
    public TaskSite FindBestSiteForPlayer(Transform player, Camera viewCam, float rayRange, LayerMask siteMask)
        => FindBestSiteForPlayer(player, viewCam, rayRange, siteMask, 0f);

    /// <summary>
    /// The site the camera is looking at (raycast through the viewport centre), else the nearest site
    /// within maxDistance of the player (maxDistance &lt;= 0 = unlimited). Null when nothing qualifies.
    /// </summary>
    public TaskSite FindBestSiteForPlayer(Transform player, Camera viewCam, float rayRange, LayerMask siteMask, float maxDistance)
    {
        // 1) Try what the camera is looking at
        if (viewCam)
        {
            Ray ray = viewCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out var hit, rayRange, siteMask, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.GetComponentInParent<TaskSite>();
                if (t) return t;
            }
        }

        // 2) Fallback to the nearest site (respecting the distance cap)
        if (player == null) return null;
        return FindNearestSite(player.position, maxDistance);
    }
}
