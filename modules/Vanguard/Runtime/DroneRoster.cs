using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Live linkable flights on this peer. Launch ownership is captured from the launching player,
    /// and the host owns the share flag and the single remote pilot seat.
    /// </summary>
    internal static class DroneRoster
    {
        internal sealed class Entry
        {
            public Missile Missile;
            public VanguardRole Role;
            public string Callsign;
            public float SeenAt;
            public uint OwnerId;
            public uint PilotId;
            public uint Lease, SentSequence;
            public bool Shared;
            public Player Pilot;
            public Aircraft PilotAircraft;
            private uint inputSequence;
            private float lastInput;
            private bool hasInput;
#if !PUBLIC_RELEASE
            internal bool AcceptedInput => hasInput;
#endif

            public bool Alive => Missile != null && !Missile.disabled;
            public bool Occupied => PilotId != 0;
            public bool Friendly(Player player) => Alive && player != null && player.HQ != null && player.HQ == Missile.NetworkHQ;
            public bool Owner(Player player) => OwnerId != 0 && Friendly(player) && OwnerId == PlayerId(player);
            public bool VisibleTo(Player player) => Friendly(player) && (Shared || Owner(player));
            public bool ControlledBy(Player player) => PilotId != 0 && PilotId == PlayerId(player);

            public void SetPilot(Player player)
            {
                Pilot = player;
                PilotAircraft = player != null ? player.Aircraft : null;
                PilotId = PlayerId(player);
                Lease++;
                SentSequence = inputSequence = 0;
                lastInput = Time.unscaledTime;
                hasInput = false;
            }

            internal bool ValidPilot => Pilot != null && Pilot.Aircraft == PilotAircraft && VisibleTo(Pilot) && InRange(Pilot, this) &&
                (Pilot.IsHostPlayer || Pilot.Identity.Owner is { IsConnected: true, IsAuthenticated: true });

            internal bool InputExpired(float now) => now - lastInput > (hasInput ? 2f : 5f);

            internal bool AcceptInput(uint sequence, float now)
            {
                if (unchecked((int)(sequence - inputSequence)) <= 0) return false;
                inputSequence = sequence;
                lastInput = now;
                hasInput = true;
                return true;
            }

            internal void Mirror(uint ownerId, uint pilotId, uint lease, bool shared)
            {
                if (Lease != lease) SentSequence = 0;
                OwnerId = ownerId;
                PilotId = pilotId;
                Lease = lease;
                Shared = shared;
            }
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<Missile, Entry> ByMissile = new Dictionary<Missile, Entry>();
        private static readonly Dictionary<(Unit, string), int> Counts = new Dictionary<(Unit, string), int>(); // per owner and jsonKey: MANTA counts apart from REMORA

        internal static uint PlayerId(Player player) => player != null && player.Identity != null ? player.Identity.NetId : 0;

        internal static bool TryGet(Missile missile, out Entry entry)
        {
            entry = null;
            return missile != null && ByMissile.TryGetValue(missile, out entry) && entry.Alive;
        }

        public static void Track(Missile missile)
        {
            VanguardRole role = VanguardKeys.RoleOf(missile.definition.jsonKey);
            if (!VanguardKeys.IsSteerable(role) || ByMissile.ContainsKey(missile)) return;
            var key = (missile.owner, missile.definition.jsonKey);
            Counts.TryGetValue(key, out int n);
            Counts[key] = ++n;
            var entry = new Entry
            {
                Missile = missile, Role = role, Callsign = DroneLink.Callsign(key.Item2, role, n), SeenAt = Time.time,
                OwnerId = key.Item1 is Aircraft launcher && launcher != null ? PlayerId(launcher.Player) : 0,
            };
            ByMissile.Add(missile, entry);
            Entries.Add(entry);
        }

        public static void Visible(Player player, List<Entry> into)
        {
            into.Clear();
            foreach (Entry entry in Entries)
                if (entry.VisibleTo(player)) into.Add(entry);
        }

        internal static Entry ControlledBy(Player player)
        {
            foreach (Entry entry in Entries)
                if (entry.VisibleTo(player) && entry.ControlledBy(player)) return entry;
            return null;
        }

        internal static Player ActivePilot(Missile missile) => TryGet(missile, out Entry entry) && entry.ValidPilot ? entry.Pilot : null;

        internal static bool InRange(Player player, Entry entry)
        {
            Aircraft aircraft = player != null ? player.Aircraft : null;
            return entry.Alive && DronePilot.SupportsLinkAircraft(aircraft) &&
                DroneLink.InRange(FastMath.Distance(aircraft.GlobalPosition(), entry.Missile.GlobalPosition()), WatchtowerPod.LinkScale(aircraft));
        }

        internal static IReadOnlyList<Entry> LiveEntries => Entries;

        internal static void Prune()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry entry = Entries[i];
                if (entry.Alive) continue;
                ByMissile.Remove(entry.Missile);
                Entries.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            Entries.Clear();
            ByMissile.Clear();
            Counts.Clear();
        }
    }
}
