using System.Collections.Generic;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>
    /// Where the built-in stations transmit from. A commercial station sits at the player's
    /// capital, the world service at another faction's, and the forces broadcast at the
    /// nearest base the player's side still holds — so flying away from your own airfield
    /// really does cost you the station.
    ///
    /// <para>Resolved on a timer, never per frame, and bounded to the handful of anchors the
    /// three built-ins need. A user folder has no transmitter: it is a local archive, and
    /// reception for it is always perfect.</para>
    /// </summary>
    internal sealed class RadioTransmitterAnchors
    {
        private const float RefreshSeconds = 10f;
        private const float HqAntennaMetres = 40f;
        private const float BaseAntennaMetres = 25f;

        private struct Anchor
        {
            public Vector3 Position;
            public float Height;
        }

        private readonly Dictionary<string, Anchor> anchors =
            new Dictionary<string, Anchor>(System.StringComparer.Ordinal);
        private float nextRefresh;
        private bool listenerValid;
        private Vector3 listenerPosition;
        private float listenerHeight;

        public bool HasListener => listenerValid;
        public Vector3 ListenerPosition => listenerPosition;
        public float ListenerHeight => listenerHeight;

        public void Reset()
        {
            anchors.Clear();
            nextRefresh = 0f;
            listenerValid = false;
        }

        public void Tick(float now, bool force = false)
        {
            if (!force && now < nextRefresh) return;
            nextRefresh = now + RefreshSeconds;
            Refresh();
        }

        public bool TryGet(string stationId, out Vector3 position, out float height)
        {
            position = Vector3.zero;
            height = 0f;
            if (stationId == null || !anchors.TryGetValue(stationId, out Anchor anchor))
                return false;
            position = anchor.Position;
            height = anchor.Height;
            return true;
        }

        private void Refresh()
        {
            anchors.Clear();
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
            if (own != null)
                Add(BuiltInStationRules.AgrapolId, own.transform.position, HqAntennaMetres);

            try
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null || hq == own) continue;
                    Add(BuiltInStationRules.MarisId, hq.transform.position, HqAntennaMetres);
                    break;
                }
            }
            catch { }

            Airbase nearest = NearestOwnedAirbase(own);
            if (nearest != null)
                Add(BuiltInStationRules.BaseId, nearest.transform.position, BaseAntennaMetres);
        }

        private void Add(string id, Vector3 position, float height)
        {
            if (anchors.ContainsKey(id)) return;
            anchors[id] = new Anchor { Position = position, Height = height };
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
