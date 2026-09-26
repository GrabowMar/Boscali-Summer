using System.Collections.Generic;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>One station's transmitting site: where it is and how high.</summary>
    internal readonly struct RadioTower
    {
        public Vector3 Position { get; }
        public float Height { get; }

        public RadioTower(Vector3 position, float height)
        {
            Position = position;
            Height = height;
        }
    }

    /// <summary>
    /// Where the built-in stations transmit from. Republic radio sits at Boscali HQ, state
    /// radio at Primeva/PALA HQ, and the forces broadcast at the
    /// nearest base the player's side still holds — so flying away from your own airfield
    /// really does cost you the station, and losing the base takes it off the air entirely.
    ///
    /// <para>Resolved on a timer, never per frame, and bounded to the handful of anchors the
    /// three built-ins need. A user folder has no transmitter: it is a local archive, and
    /// reception (and tower loss) do not apply to it.</para>
    /// </summary>
    internal sealed class RadioTransmitterAnchors
    {
        private const float RefreshSeconds = 10f;
        private const float HqAntennaMetres = 40f;
        private const float BaseAntennaMetres = 25f;

        private readonly Dictionary<string, RadioTower> towers =
            new Dictionary<string, RadioTower>(System.StringComparer.Ordinal);
        private float nextRefresh;
        private bool listenerValid;
        private Vector3 listenerPosition;
        private float listenerHeight;

        public bool HasListener => listenerValid;
        public Vector3 ListenerPosition => listenerPosition;
        public float ListenerHeight => listenerHeight;

        /// <summary>
        /// True once the scene produced at least one authored transmitter, which is what makes
        /// a missing one meaningful (the tower is gone) instead of merely unknown (a menu, a
        /// map without the anchor, or a player who has not spawned yet).
        /// </summary>
        public bool MapResolved { get; private set; }

        public void Reset()
        {
            towers.Clear();
            nextRefresh = 0f;
            listenerValid = false;
            MapResolved = false;
        }

        public void Tick(float now, bool force = false)
        {
            if (!force && now < nextRefresh) return;
            nextRefresh = now + RefreshSeconds;
            Refresh();
        }

        public bool TryGet(string stationId, out RadioTower tower)
        {
            tower = default;
            return stationId != null && towers.TryGetValue(stationId, out tower);
        }

        private void Refresh()
        {
            towers.Clear();
            MapResolved = false;
            listenerValid = false;

            Player local = null;
            try
            {
                if (!GameManager.GetLocalPlayer<Player>(out local) || local == null) return;
            }
            catch
            {
                return;
            }

            try
            {
                Aircraft aircraft = local.Aircraft;
                if (aircraft != null)
                {
                    listenerPosition = aircraft.transform.position;
                    listenerHeight = listenerPosition.y;
                    listenerValid = true;
                }
            }
            catch
            {
                listenerValid = false;
            }

            FactionHQ own = local.HQ;
            FactionHQ other = null;
            try
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null) continue;
                    if (hq != own && other == null) other = hq;
                    string faction = FactionName(hq);
                    if (faction.IndexOf("Boscali", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        Add(BuiltInStationRules.AgrapolId, hq.transform.position, HqAntennaMetres);
                    else if (faction.IndexOf("Primeva", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                             faction.IndexOf("PALA", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        Add(BuiltInStationRules.MarisId, hq.transform.position, HqAntennaMetres);
                }
            }
            catch { }

            if (towers.Count == 0)
            {
                if (own != null)
                    Add(BuiltInStationRules.AgrapolId, own.transform.position, HqAntennaMetres);
                if (other != null)
                    Add(BuiltInStationRules.MarisId, other.transform.position, HqAntennaMetres);
            }

            Airbase nearest = NearestOwnedAirbase(own);
            if (nearest != null)
                Add(BuiltInStationRules.BaseId, nearest.transform.position, BaseAntennaMetres);

            MapResolved = towers.Count > 0;
        }

        private void Add(string id, Vector3 position, float height)
        {
            if (towers.ContainsKey(id)) return;
            towers[id] = new RadioTower(position, height);
        }

        private static string FactionName(FactionHQ hq)
        {
            try
            {
                string name = hq.faction == null ? null : hq.faction.factionName;
                return string.IsNullOrWhiteSpace(name) ? "COMMAND" : name;
            }
            catch
            {
                return "COMMAND";
            }
        }

        private Airbase NearestOwnedAirbase(FactionHQ owner)
        {
            Airbase found = null;
            float best = float.MaxValue;
            try
            {
                var lookup = FactionRegistry.airbaseLookup;
                if (lookup == null) return null;
                foreach (Airbase airbase in lookup.Values)
                {
                    if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed())
                        continue;
                    if (owner != null && airbase.CurrentHQ != owner) continue;
                    float distance = listenerValid
                        ? (airbase.transform.position - listenerPosition).sqrMagnitude
                        : 0f;
                    if (distance >= best) continue;
                    best = distance;
                    found = airbase;
                }
            }
            catch { }
            return found;
        }
    }
}
