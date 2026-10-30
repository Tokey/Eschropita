using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Animates the life-support plant from <see cref="LifeSupport"/>: tank gauges fill with the O₂ level and take its
    /// status colour, the pump fan spins with the windmill's power, light pulses run along the conduits (speed and
    /// colour from power, a bright surge when the windmill is repaired), the panel screen shows status, vents breathe
    /// steam, and below 25 % the alarm lamp turns and sweeps red light. Dark before life support switches on.
    /// </summary>
    public class LifeSupportPlantRig : MonoBehaviour
    {
        public Transform[] gauges = new Transform[0];
        public readonly List<Material> gaugeMaterials = new List<Material>();
        public Transform fan, lamp;
        public Material screen, lampMaterial;
        public Light alarmSpot;
        public Renderer[] bands = new Renderer[0];
        public Vector3[] vents = new Vector3[0];

        MaterialPropertyBlock _mpb;
        float _flowPhase, _surge, _fanSpeed, _nextSteam, _value = 1f;
        static readonly int Emissive = Shader.PropertyToID("_EmissiveColor");

        void OnEnable() => GameSignals.SiteRepaired += OnRepaired;
        void OnDisable() => GameSignals.SiteRepaired -= OnRepaired;
        void OnRepaired(TaskSite s) { if (s != null && s.type == TaskType.PowerPlant) _surge = 1f; }

        void Update()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            float dt = Time.deltaTime, t = Time.time;
            var life = LifeSupport.Instance;
            bool active = life != null && life.Active;
            float v = active ? life.Value01 : 1f;
            float power = life != null ? life.PowerLevel01 : 1f;
            _value = Mathf.Lerp(_value, v, 1f - Mathf.Exp(-dt * 3f));
            Color status = OverhaulPalette.Status(_value);
            float glow = active ? 1f : 0.35f;

            for (int i = 0; i < gauges.Length; i++)
            {
                if (gauges[i] == null) continue;
                float wobble = 1f + 0.02f * Mathf.Sin(t * 2f + i);
                gauges[i].localScale = new Vector3(1f, Mathf.Max(0.02f, 1.9f * _value * wobble), 1f);
                if (i < gaugeMaterials.Count) OverhaulMaterials.SetEmissive(gaugeMaterials[i], status * (2.5f * glow));
            }

            _fanSpeed = Mathf.MoveTowards(_fanSpeed, 60f + 540f * power, dt * 220f);
            if (fan != null) fan.localRotation *= Quaternion.AngleAxis(_fanSpeed * dt, Vector3.forward);

            float flicker = 0.9f + 0.1f * Mathf.Sin(t * 23f) * Mathf.Sin(t * 3.7f);
            if (screen != null) OverhaulMaterials.SetEmissive(screen, status * (2.2f * flicker * glow));

            // pulses along the conduits
            _flowPhase += dt * (1.2f + 5f * power) * (1f + 2f * _surge);
            _surge = Mathf.MoveTowards(_surge, 0f, dt * 0.4f);
            Color flowCol = Color.Lerp(OverhaulPalette.Status(power), Color.white, _surge * 0.5f);
            for (int i = 0; i < bands.Length; i++)
            {
                var r = bands[i];
                if (r == null) continue;
                float wave = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(_flowPhase - i * 0.55f), 6f);
                float k = (0.25f + 2.6f * wave + 3f * _surge) * glow * (power < 0.05f ? 0.3f : 1f);
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(Emissive, flowCol * k);
                r.SetPropertyBlock(_mpb);
            }

            // alarm lamp
            bool alarm = active && _value < 0.25f;
            if (lamp != null && alarm) lamp.localRotation *= Quaternion.AngleAxis(240f * dt, Vector3.up);
            if (lampMaterial != null) OverhaulMaterials.SetEmissive(lampMaterial, alarm ? new Color(5f, 0.6f, 0.1f) : (active ? new Color(0.4f, 0.3f, 0.05f) : Color.black));
            if (alarmSpot != null) OverhaulLights.SetLumens(alarmSpot, alarm ? 6000f : 0f);

            // steam
            if (active && t >= _nextSteam && vents.Length > 0)
            {
                _nextSteam = t + Mathf.Lerp(2.5f, 0.8f, power) * Random.Range(0.8f, 1.2f);
                Vector3 p = vents[Random.Range(0, vents.Length)];
                VfxKit.Dust(p, 3, new Color(0.92f, 0.9f, 0.88f, 0.45f), 0.55f, 0.6f);
            }
        }
    }
}
