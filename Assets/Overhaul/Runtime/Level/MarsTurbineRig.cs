using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Brings the Mars turbine to life from the windmill TaskSite's state: rotor speed follows its health (smooth
    /// spin-up / coast-down, gusts, faster in storms); when power is lost the rotor stops, the nacelle sags, it sparks
    /// and smokes and the beacon blinks urgently; Fixers' repairs grow glowing fungal patches that stay (repair as
    /// reciprocity); a full repair flashes the beacon green; shard-shield plates pop on as the shield builds and drop
    /// when it breaks; tornado hits shake the tower. Reports rotor speed for the audio.
    /// </summary>
    public class MarsTurbineRig : MonoBehaviour
    {
        public Transform nacelle, rotor, anemometer;
        public Light beaconLight;
        public Material beaconMaterial;
        public Transform[] plates = new Transform[0];
        public Transform[] fungi = new Transform[0];
        public float towerHeight = 11f;

        TaskSite _site;
        float _speed, _growth, _lastHealth = -1f, _shake, _greenUntil, _nextSpark, _nextSmoke, _sag, _yaw0;
        readonly float[] _plateScale = new float[16];
        int _platesShown;

        public float RotorSpeed01 => Mathf.Clamp01(_speed / 170f);

        void Start()
        {
            _yaw0 = nacelle != null ? nacelle.localEulerAngles.y : 0f;
        }

        void OnEnable()
        {
            GameSignals.SiteRepaired += OnRepaired;
            GameSignals.SiteShieldBroken += OnShieldBroken;
            GameSignals.TornadoHit += OnTornadoHit;
        }

        void OnDisable()
        {
            GameSignals.SiteRepaired -= OnRepaired;
            GameSignals.SiteShieldBroken -= OnShieldBroken;
            GameSignals.TornadoHit -= OnTornadoHit;
            OverhaulLandmarks.TurbineRotorSpeed01 = -1f;
        }

        void OnRepaired(TaskSite s) { if (s != null && s == _site) _greenUntil = Time.time + 2.5f; }

        void OnShieldBroken(TaskSite s)
        {
            if (s == null || s != _site) return;
            foreach (var p in plates)
                if (p != null && p.gameObject.activeSelf) VfxKit.Shards(p.position, 6, OverhaulPalette.ShardAmber, 4f);
            for (int i = 0; i < plates.Length; i++) { if (plates[i] != null) plates[i].gameObject.SetActive(false); _plateScale[i] = 0f; }
            _platesShown = 0;
        }

        void OnTornadoHit(Vector3 p)
        {
            Vector3 d = p - transform.position; d.y = 0f;
            if (d.magnitude < 12f) _shake = 1f;
        }

        void Update()
        {
            if (_site == null)
            {
                var bridge = SignalBridge.Instance;
                _site = bridge != null ? bridge.Windmill : null;
            }
            float dt = Time.deltaTime;
            float h = _site != null ? _site.Health01 : 1f;
            bool dead = _site != null && _site.IsDestroyed;
            var bridge2 = SignalBridge.Instance;
            bool storm = bridge2 != null && bridge2.Tornado != null && bridge2.Tornado.IsActive;
            float t = Time.time;

            // --- rotor ---
            float gust = 1f + 0.12f * Mathf.Sin(t * 0.7f) + 0.06f * Mathf.Sin(t * 2.3f);
            float target = dead ? 0f : Mathf.Lerp(30f, 170f, h) * gust * (storm ? 1.35f : 1f);
            float accel = target > _speed ? 45f : 22f;
            _speed = Mathf.MoveTowards(_speed, target, accel * dt);
            if (rotor != null) rotor.localRotation *= Quaternion.AngleAxis(_speed * dt, Vector3.forward);
            if (anemometer != null) anemometer.localRotation *= Quaternion.AngleAxis((storm ? 520f : 220f) * (0.3f + h) * dt, Vector3.up);
            OverhaulLandmarks.TurbineRotorSpeed01 = RotorSpeed01;

            // --- nacelle: slow yaw into the wind, a sag when dead, shake on hits ---
            _sag = Mathf.MoveTowards(_sag, dead ? 7f : 0f, dt * 4f);
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 1.5f);
            if (nacelle != null)
            {
                float yaw = _yaw0 + 10f * Mathf.Sin(t * 0.06f) + 3f * Mathf.Sin(t * 0.17f);
                float wob = _shake * 3f * Mathf.Sin(t * 37f);
                nacelle.localRotation = Quaternion.Euler(_sag + wob, yaw, wob * 0.5f);
            }

            // --- beacon ---
            Color c;
            float on;
            if (t < _greenUntil) { c = new Color(0.3f, 1f, 0.45f); on = 0.6f + 0.4f * Mathf.Sin(t * 12f); }
            else if (dead) { c = new Color(1f, 0.12f, 0.08f); on = Mathf.Repeat(t * 3f, 1f) < 0.5f ? 1f : 0.05f; }
            else { c = new Color(1f, 0.15f, 0.1f); on = Mathf.Repeat(t * 0.5f, 1f) < 0.18f ? 1f : 0.08f; }
            if (beaconMaterial != null) OverhaulMaterials.SetEmissive(beaconMaterial, c * (6f * on));
            if (beaconLight != null) { beaconLight.color = c; OverhaulLights.SetLumens(beaconLight, 450f * on); }

            // --- failure sparks / smoke ---
            if (dead && nacelle != null)
            {
                if (t >= _nextSpark) { _nextSpark = t + Random.Range(0.4f, 1.1f); VfxKit.Sparks(nacelle.position + Vector3.up * 0.3f, 8, new Color(1f, 0.55f, 0.2f), 4f); }
                if (t >= _nextSmoke) { _nextSmoke = t + 0.35f; VfxKit.Smoke(nacelle.position + Vector3.up * 0.4f, 1, 0.7f); }
            }

            // --- fungal repair patches: grow with repairs, stay afterwards, shrink a little when dismantled ---
            if (_site != null)
            {
                if (_lastHealth >= 0f)
                {
                    float dh = h - _lastHealth;
                    if (dh > 0f) _growth = Mathf.Clamp01(_growth + dh * 1.4f);
                    else if (dh < 0f) _growth = Mathf.Clamp01(_growth + dh * 0.35f);
                }
                _lastHealth = h;
            }
            for (int i = 0; i < fungi.Length; i++)
            {
                if (fungi[i] == null) continue;
                float local = Mathf.Clamp01(_growth * fungi.Length - i * 0.7f);
                float s = Mathf.SmoothStep(0f, 1f, local) * (1f + 0.05f * Mathf.Sin(t * 1.7f + i));
                fungi[i].localScale = new Vector3(1f, 1f, 0.6f) * Mathf.Max(0.001f, s);
            }

            // --- shard-shield plates ---
            int want = 0;
            if (_site != null && _site.maxShield > 0f) want = Mathf.CeilToInt(_site.Shield01 * plates.Length - 0.001f);
            for (int i = 0; i < plates.Length; i++)
            {
                var p = plates[i];
                if (p == null) continue;
                bool show = i < want;
                if (show && !p.gameObject.activeSelf)
                {
                    p.gameObject.SetActive(true);
                    _plateScale[i] = 0f;
                    VfxKit.Motes(p.position, 10, OverhaulPalette.ShardAmber, 0.4f, 1f);
                }
                if (!show && p.gameObject.activeSelf) { p.gameObject.SetActive(false); _plateScale[i] = 0f; }
                if (show)
                {
                    _plateScale[i] = Mathf.MoveTowards(_plateScale[i], 1f, dt * 3f);
                    float k = _plateScale[i];
                    float pop = k < 1f ? 1f + Mathf.Sin(k * Mathf.PI) * 0.3f : 1f;
                    p.localScale = Vector3.one * (k * pop);
                }
            }
            _platesShown = want;
        }
    }
}
