using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The local pilot's landing zone: set from the big map's right-click card, it stands as the
    /// aim of every HALO stick and Vanguard glider that pilot releases until it is moved or
    /// cleared. Implemented by Urban Combat's zone net, which also carries it to the host;
    /// absent (that module off) the map offers no zone actions and releases keep their
    /// designated targets.
    /// </summary>
    internal interface ILandingZone
    {
        /// <summary>This pilot has a zone set for the aircraft they currently fly.</summary>
        bool HasLocal { get; }

        /// <summary>The local zone in global coordinates (ground height resolved by the host).</summary>
        bool TryGetLocal(out GlobalPosition point);

        /// <summary>Moves the local zone to a map ground point and tells the host.</summary>
        void SetLocal(GlobalPosition point);

        /// <summary>Clears the local zone and tells the host.</summary>
        void ClearLocal();

        /// <summary>How far a release at this height above the zone reaches, in flat metres: a HALO stick (<paramref name="halo"/>) or a Hamilcar glide. The pilot's drop cue.</summary>
        float ReachM(bool halo, float heightAboveZone);
    }

    /// <summary>
    /// Host-side landing zones by launching aircraft, plus every peer's prediction copy (HALO
    /// sticks are planned on all peers from the same aim). A zone is read once, at release:
    /// moving it never diverts a stick or glider already airborne. Dead launchers lapse on
    /// read, so a respawn starts clean.
    /// </summary>
    internal static class LandingZones
    {
        private static readonly Dictionary<Unit, GlobalPosition> ByLauncher = new Dictionary<Unit, GlobalPosition>();
        private static readonly List<Unit> Stale = new List<Unit>();

        public static bool TryGet(Unit launcher, out GlobalPosition point)
        {
            point = default;
            if (launcher == null || !ByLauncher.TryGetValue(launcher, out point)) return false;
            if (!launcher.disabled) return true;
            ByLauncher.Remove(launcher);
            return false;
        }

        /// <summary>Sets (<paramref name="point"/> given) or clears (null) a launcher's zone.</summary>
        public static void Order(Unit launcher, GlobalPosition? point)
        {
            if (launcher == null) return;
            Stale.Clear();
            foreach (KeyValuePair<Unit, GlobalPosition> pair in ByLauncher)
                if (pair.Key == null || pair.Key.disabled) Stale.Add(pair.Key);
            for (int i = 0; i < Stale.Count; i++) ByLauncher.Remove(Stale[i]);
            if (point.HasValue) ByLauncher[launcher] = point.Value;
            else ByLauncher.Remove(launcher);
        }

        public static void Clear() => ByLauncher.Clear();
    }
}
