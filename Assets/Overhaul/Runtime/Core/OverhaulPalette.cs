using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// The overhaul's shared colour language. Three families:
    ///  - JoyTech (the failed colonial project): dusty off-white panels, chrome, faded corporate teal + magenta,
    ///    hazard amber. Everything human-made is weathered and stained by red dust.
    ///  - Mycari / fungal: bioluminescent cyan-teal and violet (matches the painted mushroom sprites), with warm
    ///    spore gold as an accent. Organic, soft, glowing.
    ///  - Status (matches the HUD): good mint, warn amber, bad coral-red.
    /// Mycari role colours follow the design doc: blue = Fixer (repairs), red = Breaker (destroys),
    /// green = Wanderer (leaves through the door). Light paths use the same colours so the tones/hues hint
    /// which Mycari goes where.
    /// HDR glow values are linear colours meant to be multiplied by an intensity.
    /// </summary>
    public static class OverhaulPalette
    {
        // Mars
        public static readonly Color MarsDust = new Color(0.78f, 0.45f, 0.27f);
        public static readonly Color MarsDeep = new Color(0.42f, 0.18f, 0.10f);
        public static readonly Color MarsSkyDay = new Color(0.86f, 0.62f, 0.44f);   // butterscotch
        public static readonly Color MarsSunsetBlue = new Color(0.36f, 0.55f, 0.82f);
        public static readonly Color Regolith = new Color(0.55f, 0.30f, 0.19f);

        // JoyTech
        public static readonly Color JoyWhite = new Color(0.86f, 0.83f, 0.78f);
        public static readonly Color JoyGrey = new Color(0.46f, 0.47f, 0.50f);
        public static readonly Color JoyDark = new Color(0.16f, 0.17f, 0.20f);
        public static readonly Color JoyTeal = new Color(0.16f, 0.62f, 0.62f);
        public static readonly Color JoyMagenta = new Color(0.78f, 0.24f, 0.55f);
        public static readonly Color HazardAmber = new Color(0.93f, 0.62f, 0.13f);
        public static readonly Color Rust = new Color(0.55f, 0.25f, 0.12f);
        public static readonly Color Chrome = new Color(0.78f, 0.80f, 0.84f);

        // Fungal / Mycari
        public static readonly Color SporeCyan = new Color(0.25f, 0.95f, 1.00f);
        public static readonly Color SporeTeal = new Color(0.10f, 0.80f, 0.70f);
        public static readonly Color SporeViolet = new Color(0.62f, 0.32f, 1.00f);
        public static readonly Color SporeMagenta = new Color(0.95f, 0.35f, 0.90f);
        public static readonly Color SporeGold = new Color(1.00f, 0.78f, 0.38f);
        public static readonly Color MyceliumCream = new Color(0.93f, 0.88f, 0.76f);
        public static readonly Color FungalSoil = new Color(0.20f, 0.12f, 0.12f);
        public static readonly Color MossViolet = new Color(0.30f, 0.20f, 0.36f);

        // Mycari roles (doc: blue repairs, red destroys, green wanders)
        public static readonly Color RoleFixer = new Color(0.25f, 0.62f, 1.00f);
        public static readonly Color RoleBreaker = new Color(1.00f, 0.30f, 0.25f);
        public static readonly Color RoleWanderer = new Color(0.35f, 1.00f, 0.45f);

        // Obelisk shard amber (matches ObeliskShard default #E8A85C)
        public static readonly Color ShardAmber = new Color(0.910f, 0.659f, 0.361f);

        // Status (matches GameHUD good / warn / bad)
        public static readonly Color Good = new Color32(0x7F, 0xD8, 0xA6, 0xFF);
        public static readonly Color Warn = new Color32(0xE8, 0xC1, 0x5C, 0xFF);
        public static readonly Color Bad = new Color32(0xE8, 0x6A, 0x5C, 0xFF);
        public static readonly Color HudText = new Color32(0xF2, 0xEF, 0xE9, 0xFF);
        public static readonly Color HudAccent = new Color32(0xE8, 0xA8, 0x5C, 0xFF);

        public static Color ForRole(NPCRole role)
        {
            switch (role)
            {
                case NPCRole.Worker: return RoleFixer;
                case NPCRole.Attacker: return RoleBreaker;
                default: return RoleWanderer;
            }
        }

        /// <summary>Good → warn → bad as value01 falls (1 = healthy).</summary>
        public static Color Status(float value01)
        {
            value01 = Mathf.Clamp01(value01);
            return value01 > 0.5f ? Color.Lerp(Warn, Good, (value01 - 0.5f) * 2f) : Color.Lerp(Bad, Warn, value01 * 2f);
        }

        public static Color Hdr(Color c, float intensity) => new Color(c.r * intensity, c.g * intensity, c.b * intensity, c.a);
    }
}
