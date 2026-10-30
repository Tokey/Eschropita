using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Difficulty tuning, applied at runtime so the scene's values stay untouched (disable this component to get the
    /// original numbers back). Paced around winning a Fixer over at the grove before it repairs anything:
    ///  - Life support drains gently while the windmill is down: from full, about 75 s to the 25 % alarm with the
    ///    windmill at a quarter health, then an emergency reserve halves the drain (about 50 s more). That is room for
    ///    one Mycari's song, a miss and a second try, the walk and the repair. Recovery is quick once it turns again.
    ///  - Tornadoes chip the windmill moderately: one that goes for it (TornadoCalamity.seekChance) sits over it for
    ///    about 12 s and takes roughly 30 % of its health (a shard shield absorbs it first; one pass never takes more
    ///    than TornadoCalamity.maxPassDamage01). Two of those and life support starts to drain, slowly at first.
    /// Checked with an event simulation of the sandbox and obelisk phases (one song per Mycari job, Breakers taking a
    /// whole obelisk layer per trip): about 6 minutes from the sandbox to the fallen obelisk and 4-5 songs; life
    /// support runs out in about 0.5 % of runs for a sure player, 1.5 % for an average one and 5 % for one who
    /// misses a lot. Checkpoints cover the rest.
    /// Melody lengths live with the song itself (MycariPersuasion).
    /// </summary>
    [OverhaulModule(5)]
    public class GameBalance : MonoBehaviour
    {
        [Header("Life support")]
        public float drainUnpowered = 2.0f;
        public float regenPowered = 3.5f;
        [Range(0f, 1f)] public float emergencyBelow = 0.25f;
        [Range(0.1f, 1f)] public float emergencyDrainScale = 0.5f;

        [Header("Tornado")]
        public float tornadoSiteDamagePerSecond = 2.5f;

        bool _emergency, _tornadoTuned;

        void Update()
        {
            var life = LifeSupport.Instance;
            if (life != null)
            {
                bool emergency = life.Active && life.Value01 < emergencyBelow;
                life.drainPerSecondUnpowered = drainUnpowered * (emergency ? emergencyDrainScale : 1f);
                life.regenPerSecondPowered = regenPowered;
                if (emergency && !_emergency)
                {
                    var hud = GameHUD.Instance;
                    if (hud != null) hud.Toast("Emergency O<sub>2</sub> reserve: drain slowed", 3f);
                }
                _emergency = emergency;
            }

            if (!_tornadoTuned)
            {
                var bridge = SignalBridge.Instance;
                if (bridge != null && bridge.Tornado != null)
                {
                    bridge.Tornado.siteDamagePerSecond = tornadoSiteDamagePerSecond;
                    _tornadoTuned = true;
                }
            }
        }
    }
}
