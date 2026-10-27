using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Every peer's list of live steerable Vanguard flights, for the drone link (UAV terminal, UAV MFD, feed). Filled from
    /// Missile.StartMissile, which runs on every peer, so an MP client sees its own drones (VanguardRegistry is host-only).
    /// Each entry keeps its role, when this peer first saw it and a per-owner, per-type callsign (REMORA 2).
    /// </summary>
    internal static class DroneRoster
    {
        internal sealed class Entry
        {
            public Missile Missile;
            public VanguardRole Role;
            public string Callsign;
            public float SeenAt;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<(Unit, string), int> Counts = new Dictionary<(Unit, string), int>(); // per owner and jsonKey: MANTA counts apart from REMORA

        public static void Track(Missile missile)
        {
            VanguardRole role = VanguardKeys.RoleOf(missile.definition.jsonKey);
            if (!VanguardKeys.IsSteerable(role)) return;
            var key = (missile.owner, missile.definition.jsonKey);
            Counts.TryGetValue(key, out int n);
            Counts[key] = ++n;
            Entries.Add(new Entry { Missile = missile, Role = role, Callsign = DroneLink.Callsign(key.Item2, role, n), SeenAt = Time.time });
        }

        /// <summary>The live drones of <paramref name="owner"/>, in launch order. Dead ones leave the roster here.</summary>
        public static void Mine(Unit owner, List<Entry> into)
        {
            into.Clear();
            for (int i = Entries.Count - 1; i >= 0; i--)
                if (Entries[i].Missile == null || Entries[i].Missile.disabled) Entries.RemoveAt(i);
            if (owner == null) return;
            foreach (Entry e in Entries)
                if (e.Missile.owner == owner) into.Add(e);
        }

        public static void Clear()
        {
            Entries.Clear();
            Counts.Clear();
        }
    }
}
