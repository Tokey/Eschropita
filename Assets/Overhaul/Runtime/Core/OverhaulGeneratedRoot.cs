using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Sits on the root of the baked level dressing ("== JoyTech Overhaul (generated) =="). Remembers which of the
    /// scene's original renderers / colliders / objects the builder switched off (the old windmill model, the stone
    /// tile floor, ...) so Tools ▸ Eschropita ▸ Overhaul ▸ Remove Level Dressing can switch them back on.
    /// Also exposes the landmark positions the builder used, for runtime systems (light paths, lore placement).
    /// </summary>
    public class OverhaulGeneratedRoot : MonoBehaviour
    {
        public static OverhaulGeneratedRoot Instance { get; private set; }

        public List<Renderer> hiddenRenderers = new List<Renderer>();
        public List<Collider> disabledColliders = new List<Collider>();
        public List<GameObject> deactivatedObjects = new List<GameObject>();

        [Header("Landmarks used for the bake (world space)")]
        public Vector3 spawn;
        public Vector3 groveCenter;
        public Vector3 windmill;
        public Vector3 obelisk;
        public Vector3 home;
        public Vector3 domeCenter;
        public Vector3 lifeSupportPlant;
        public Vector3 shuttle;

        [TextArea(2, 12)] public string buildReport;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void RestoreOriginals()
        {
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (var c in disabledColliders) if (c != null) c.enabled = true;
            foreach (var g in deactivatedObjects) if (g != null) g.SetActive(true);
            hiddenRenderers.Clear();
            disabledColliders.Clear();
            deactivatedObjects.Clear();
        }
    }
}
