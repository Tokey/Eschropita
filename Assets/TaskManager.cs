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
        _sites.Clear();
        _sites.AddRange(FindObjectsOfType<TaskSite>());
    }

    public TaskSite FindBestSiteForPlayer(Transform player, Camera viewCam, float rayRange, LayerMask siteMask)
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

        // 2) Fallback to nearest any site
        TaskSite best = null;
        float bestD = float.MaxValue;
        foreach (var s in _sites)
        {
            float d = Vector3.SqrMagnitude(s.transform.position - player.position);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }
}
