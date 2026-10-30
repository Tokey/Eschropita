using System.Collections.Generic;
using UnityEngine;

namespace Eschropita.Overhaul
{
    /// <summary>Something new the player can use with the interact key (lore logs, terminals, …).</summary>
    public interface IOverhaulInteractable
    {
        /// <summary>World position used for range checks.</summary>
        Vector3 InteractPosition { get; }
        /// <summary>Range (metres, XZ) within which interact reaches it.</summary>
        float InteractRadius { get; }
        /// <summary>False while unavailable (not yet revealed, already open, …).</summary>
        bool CanInteract { get; }
        void Interact();
    }

    /// <summary>
    /// Registry the PlayerController consults on the interact key, after the melody pads and before recruiting /
    /// ordering Mycari, so overhaul interactables never steal a press meant for the puzzle.
    /// </summary>
    public static class OverhaulInteract
    {
        static readonly List<IOverhaulInteractable> _items = new List<IOverhaulInteractable>();

        public static void Register(IOverhaulInteractable i) { if (i != null && !_items.Contains(i)) _items.Add(i); }
        public static void Unregister(IOverhaulInteractable i) { _items.Remove(i); }

        /// <summary>The nearest available interactable in range of <paramref name="playerPos"/>, or null.</summary>
        public static IOverhaulInteractable Nearest(Vector3 playerPos)
        {
            IOverhaulInteractable best = null;
            float bestD = float.MaxValue;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it == null || (it is Object o && o == null)) { _items.RemoveAt(i); continue; }
                if (!it.CanInteract) continue;
                Vector3 d = it.InteractPosition - playerPos; d.y = 0f;
                float dist = d.magnitude;
                if (dist <= it.InteractRadius && dist < bestD) { bestD = dist; best = it; }
            }
            return best;
        }

        /// <summary>Called by PlayerController on the interact key. True = consumed.</summary>
        public static bool TryConsume(Vector3 playerPos)
        {
            var it = Nearest(playerPos);
            if (it == null) return false;
            it.Interact();
            GameSignals.RaiseInteracted(it.InteractPosition);
            return true;
        }
    }
}
