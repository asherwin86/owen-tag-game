using System.Collections.Generic;
using UnityEngine;

namespace TagGame.Juice
{
    /// <summary>Who the minimap should draw: bots, other players and pickups register here. Destroyed ones drop off.</summary>
    public static class MapBlips
    {
        public struct Blip { public Transform t; public Color c; public float size; }
        public static readonly List<Blip> Items = new List<Blip>();
        /// <summary>Whoever is IT right now (gets a red ring on the minimap).</summary>
        public static Transform It;

        public static void Register(Transform t, Color c, float size = 1f)
        {
            if (t == null) return;
            Items.Add(new Blip { t = t, c = c, size = size });
        }

        public static void Prune()
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].t == null) Items.RemoveAt(i);
        }
    }
}
