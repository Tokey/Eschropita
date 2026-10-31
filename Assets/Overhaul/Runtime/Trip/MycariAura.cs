using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The role glow a Mycari shows once the player has attuned to it (see <see cref="TripDirector"/>): a breathing
    /// disc and ring of light on the ground in its role colour (blue Fixer, red Breaker, green Wanderer) plus a soft
    /// coloured light. Always faintly visible afterwards, vivid while the spores are strong. Added at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class MycariAura : MonoBehaviour
    {
        NPCFlocker _npc;
        Transform _root;
        Material _disc, _ring;
        Transform _ringT;
        Light _light;
        float _born, _seed;
        Color _color;

        void Start()
        {
            _npc = GetComponent<NPCFlocker>();
            _color = _npc != null ? OverhaulPalette.ForRole(_npc.role) : OverhaulPalette.SporeCyan;
            _born = Time.time;
            _seed = Random.value * 10f;

            _root = new GameObject("Spore Aura").transform;
            _root.SetParent(transform, false);
            _disc = OverhaulMaterials.OverGround(OverhaulMaterials.SoftGlow("AuraDisc", _color, 1.2f, 0.95f, OverhaulMaterials.GlowShape.Disc, null, 2.2f));
            _ring = OverhaulMaterials.OverGround(OverhaulMaterials.SoftGlow("AuraRing", _color, 2.2f, 1f, OverhaulMaterials.GlowShape.Ring, null, 1.5f, 0.78f, 0.07f));
            var disc = OverhaulPrimitives.EffectRenderer(_root, "Disc", OverhaulPrimitives.GroundQuad, _disc);
            disc.transform.localScale = new Vector3(2.6f, 1f, 2.6f);
            _ringT = OverhaulPrimitives.EffectRenderer(_root, "Ring", OverhaulPrimitives.GroundQuad, _ring).transform;

            var lg = new GameObject("Aura Light");
            lg.transform.SetParent(_root, false);
            lg.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            _light = OverhaulLights.Point(lg, _color, 300f, 5f, false, false);
        }

        void LateUpdate()
        {
            if (_root == null) return;
            // Keep the glow on the ground even if the Mycari's pivot is above it.
            Vector3 p = transform.position;
            float gy = OverhaulPrimitives.GroundY(p);
            _root.position = new Vector3(p.x, Mathf.Min(p.y, gy) + 0.05f, p.z);
            _root.rotation = Quaternion.identity;

            float age = Time.time - _born;
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.6f));
            float trip = TripDirector.Intensity;
            float strength = (0.3f + 0.7f * trip) * grow;
            float breath = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f + _seed);

            _disc.SetFloat("_Intensity", (0.5f + 0.9f * strength) * (0.8f + 0.35f * breath));
            _ring.SetFloat("_Intensity", (0.8f + 2.4f * strength) * (0.7f + 0.5f * breath));
            float ringSize = Mathf.Lerp(1.6f, 3.4f, Mathf.Repeat(Time.time * 0.45f + _seed * 0.1f, 1f));
            _ringT.localScale = new Vector3(ringSize, 1f, ringSize) * grow;
            if (_light != null) OverhaulLights.SetLumens(_light, (120f + 900f * strength) * (0.75f + 0.25f * breath));
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            if (_disc != null) Destroy(_disc);
            if (_ring != null) Destroy(_ring);
        }
    }
}
