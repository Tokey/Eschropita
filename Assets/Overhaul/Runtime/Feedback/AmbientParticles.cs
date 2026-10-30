using UnityEngine;
using UnityEngine.Rendering;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Life in the air: dust motes drifting around the player that catch the light (thicker and fast-streaking in
    /// a storm), glowing spores rising over the mushroom grove (more of them when the spores are strong), and faint
    /// spores trailing near every Mycari. All looping, cheap, world-space particle systems.
    /// </summary>
    [OverhaulModule(30)]
    public class AmbientParticles : MonoBehaviour
    {
        ParticleSystem _dust, _streaks, _groveSpores;
        Material _dustMat, _streakMat, _sporeMat;
        float _storm;

        void Start()
        {
            _dustMat = OverhaulMaterials.SoftGlow("AmbientDust", new Color(1f, 0.85f, 0.65f), 1.4f, 0.6f, OverhaulMaterials.GlowShape.Disc, null, 2f);
            _streakMat = OverhaulMaterials.SoftGlow("AmbientStreaks", new Color(0.85f, 0.6f, 0.4f), 0.8f, 0.1f, OverhaulMaterials.GlowShape.Disc, null, 1.2f);
            _sporeMat = OverhaulMaterials.SoftGlow("AmbientSpores", Color.white, 2.4f, 1f, OverhaulMaterials.GlowShape.Disc, null, 1.8f);

            _dust = Loop("Air Dust", _dustMat, 400, new Vector3(36f, 10f, 36f), 22f, 9f, 0.02f, 0.07f, ParticleSystemRenderMode.Billboard);
            _streaks = Loop("Storm Streaks", _streakMat, 600, new Vector3(40f, 8f, 40f), 0f, 1.6f, 0.04f, 0.1f, ParticleSystemRenderMode.Stretch);
            var r = _streaks.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.12f;
            r.lengthScale = 2f;
            _groveSpores = Loop("Grove Spores", _sporeMat, 500, new Vector3(14f, 1.5f, 14f), 18f, 6f, 0.05f, 0.12f, ParticleSystemRenderMode.Billboard);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(OverhaulPalette.SporeCyan, 0f), new GradientColorKey(OverhaulPalette.SporeViolet, 0.5f), new GradientColorKey(OverhaulPalette.SporeGold, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var main = _groveSpores.main;
            main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            var vel = _groveSpores.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        }

        void OnDestroy()
        {
            if (_dustMat != null) Destroy(_dustMat);
            if (_streakMat != null) Destroy(_streakMat);
            if (_sporeMat != null) Destroy(_sporeMat);
        }

        ParticleSystem Loop(string name, Material mat, int max, Vector3 box, float rate, float life, float minSize, float maxSize, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startSpeed = 0f;
            main.prewarm = false;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.15f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            if (_dust == null) return;
            var bridge = SignalBridge.Instance;
            var player = bridge != null ? bridge.Player : null;
            Vector3 focus = player != null ? player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            bool storm = bridge != null && bridge.Tornado != null && bridge.Tornado.IsActive;
            _storm = Mathf.MoveTowards(_storm, storm ? 1f : 0f, Time.deltaTime * 0.4f);

            _dust.transform.position = focus + Vector3.up * 3f;
            var em = _dust.emission;
            em.rateOverTime = Mathf.Lerp(22f, 70f, _storm);

            // Storm streaks blow across the screen in the wind's direction.
            _streaks.transform.position = focus + Vector3.up * 2.5f;
            var sem = _streaks.emission;
            sem.rateOverTime = 260f * _storm;
            var main = _streaks.main;
            Vector3 wind = new Vector3(Mathf.Cos(Time.time * 0.05f), 0f, Mathf.Sin(Time.time * 0.05f)) * 14f;
            var vel = _streaks.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.8f, wind.x * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.8f);
            vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.8f, wind.z * 1.2f);

            Vector3 g = OverhaulLandmarks.GroveCenter;
            if (g != Vector3.zero)
            {
                _groveSpores.transform.position = g + Vector3.up * 0.6f;
                var gem = _groveSpores.emission;
                gem.rateOverTime = 14f + 40f * TripDirector.Intensity;
            }
        }
    }
}
