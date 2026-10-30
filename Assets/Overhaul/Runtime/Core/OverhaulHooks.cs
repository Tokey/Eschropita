using System;
using System.Collections;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Points where the scripted flow hands a beat to an overhaul system if one is installed. Each hook is null
    /// unless a module claims it, in which case the original placeholder behaviour is skipped.
    /// </summary>
    public static class OverhaulHooks
    {
        /// <summary>Bunker beat ("the Mycari are playing something new"). Return true if the overhaul played it.</summary>
        public static Func<bool> PlayBunkerSong;

        /// <summary>Runs after the final report dialogue (report card). The flow waits for the enumerator to finish.</summary>
        public static Func<IEnumerator> FinalReport;

        /// <summary>True while an overhaul UI wants the game's own HUD life-support bar hidden (the gauge replaces it).</summary>
        public static bool HideHudLifeBar;

        /// <summary>
        /// Lets the overhaul decide whether a Mycari accepts a command (e.g. Mycari you have sung with trust you).
        /// Return null to fall back to the role's acceptance table.
        /// </summary>
        public static Func<NPCFlocker, TaskCommand, bool?> AcceptOverride;

        public static void Clear()
        {
            PlayBunkerSong = null;
            FinalReport = null;
            HideHudLifeBar = false;
            AcceptOverride = null;
        }
    }
}
