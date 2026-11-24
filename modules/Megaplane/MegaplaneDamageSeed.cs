using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirage;
using Mirage.Serialization;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    // Native impact and detach RPCs are reliable deltas. A new observer needs the airframe
    // as it stands when it joins. Healthy parts are implicit; native IDs have one byte.
    [NetworkMessage]
    internal struct MegaplaneDamageSeed
    {
        internal struct Part { internal byte Id; internal float Health; internal bool Detached; }
        internal byte Protocol;
        internal uint Unit, Spawn, Layout;
        internal Part[] Changes;

        internal static void Write(NetworkWriter w, MegaplaneDamageSeed value)
        {
            w.WriteByte(value.Protocol);
            w.WritePackedUInt32(value.Unit);
            w.WritePackedUInt32(value.Spawn);
            w.WriteUInt32(value.Layout);
            w.WriteUInt16((ushort)value.Changes.Length);
            foreach (var change in value.Changes)
            {
                w.WriteByte(change.Id);
                w.WriteSingle(change.Health);
                w.WriteByte((byte)(change.Detached ? 1 : 0));
            }
        }

        internal static MegaplaneDamageSeed Read(NetworkReader r)
        {
            var result = new MegaplaneDamageSeed { Protocol = r.ReadByte() };
            if (result.Protocol != MegaplaneWire.ProtocolVersion) return result;
            result.Unit = r.ReadPackedUInt32();
            result.Spawn = r.ReadPackedUInt32();
            result.Layout = r.ReadUInt32();
            int count = r.ReadUInt16();
            if (count > 256) return default;
            result.Changes = new Part[count];
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                byte id = r.ReadByte();
                float health = r.ReadSingle();
                byte detached = r.ReadByte();
                if (id <= previous || float.IsNaN(health) || float.IsInfinity(health) || health > 100f || detached > 1)
                    return default;
                result.Changes[i] = new Part { Id = id, Health = health, Detached = detached != 0 };
                previous = id;
            }
            return result;
        }
    }

    internal sealed partial class MegaplaneDamage
    {
        private static readonly AccessTools.FieldRef<UnitPart, bool> Fragmented =
            AccessTools.FieldRefAccess<UnitPart, bool>("fragmented");
        private SortedDictionary<byte, AeroPart> damageRoster;
        private readonly Dictionary<INetworkPlayer, float> servedAt = new Dictionary<INetworkPlayer, float>();
        private uint layout;
        private float nextSeedRequest;
        private bool initializedFromHost;
        internal bool Restoring { get; private set; }

        private void Start()
        {
            // All child Awake calls have assigned the native damage indices before Start.
            var all = parts.Concat(new[] { GetComponent<AeroPart>() }).Where(p => p != null).ToArray();
            if (aircraft.damageables.Count > 256 || all.Any(p => p.id >= aircraft.damageables.Count ||
                aircraft.damageables[p.id].Damageable != (IDamageable)p) || all.Select(p => p.id).Distinct().Count() != all.Length)
            {
                Debug.LogError("[Megaplane] Native damage IDs do not fit the Groza part table.");
                return;
            }
            damageRoster = new SortedDictionary<byte, AeroPart>(all.ToDictionary(p => p.id));
            layout = 2166136261u;
            unchecked
            {
                foreach (var pair in damageRoster)
                {
                    layout = (layout ^ pair.Key) * 16777619u;
                    string name = pair.Value.gameObject == gameObject ? "airframe" : pair.Value.name;
                    foreach (char c in name) layout = (layout ^ c) * 16777619u;
                }
            }
        }

        private void RequestInitialDamage()
        {
            if (initializedFromHost || damageRoster == null || aircraft.IsServer || aircraft.NetId == 0 ||
                !aircraft.persistentID.IsValid || Time.unscaledTime < nextSeedRequest) return;
            nextSeedRequest = Time.unscaledTime + 2f;
            MegaplaneNet.Ask(aircraft, MegaplaneWire.InitialDamageVerb);
        }

        internal bool TryMakeSeed(INetworkPlayer observer, out MegaplaneDamageSeed message)
        {
            message = default;
            if (!aircraft.IsServer || damageRoster == null) return false;
            float now = Time.unscaledTime;
            if (servedAt.TryGetValue(observer, out float previous) && now - previous < 1f) return false;
            foreach (INetworkPlayer departed in servedAt.Keys.Where(p => !aircraft.Identity.observers.Contains(p)).ToArray())
                servedAt.Remove(departed);
            servedAt[observer] = now;
            var records = new List<MegaplaneDamageSeed.Part>();
            foreach (var pair in damageRoster)
            {
                AeroPart part = pair.Value;
                bool detached = part == null || part.IsDetached();
                if (detached || part.hitPoints < 100f)
                    records.Add(new MegaplaneDamageSeed.Part { Id = pair.Key, Health = part != null ? part.hitPoints : 0f, Detached = detached });
            }
            message = new MegaplaneDamageSeed { Protocol = MegaplaneWire.ProtocolVersion, Unit = aircraft.persistentID.Id,
                Spawn = aircraft.NetId, Layout = layout, Changes = records.ToArray() };
            return true;
        }

        internal void Restore(MegaplaneDamageSeed message)
        {
            if (initializedFromHost || aircraft.IsServer || damageRoster == null || message.Layout != layout ||
                message.Changes.Any(c => !damageRoster.TryGetValue(c.Id, out AeroPart p) ||
                    p == null || c.Detached && p.gameObject == gameObject)) return;
            // The reply and subsequent native deltas travel on the same ordered reliable channel.
            // A duplicate response must never overwrite damage sustained since the first.
            Restoring = true;
            try
            {
                foreach (var change in message.Changes)
                {
                    AeroPart part = damageRoster[change.Id];
                    if (part.hitPoints == change.Health) continue;
                    part.hitPoints = change.Health;
                    if (!change.Detached && !part.IsDetached())
                        part.InvokeDamage(0f, 0f, 0f, 0f);
                }
                foreach (var change in message.Changes)
                {
                    AeroPart part = damageRoster[change.Id];
                    if (!change.Detached || part.IsDetached()) continue;
                    // Recreate native child flags, mount subscriptions and detached rigidbody, but
                    // old debris and particles belonged to a time before this peer saw the carrier.
                    Fragmented(part) = true;
                    part.Detach(aircraft.rb.GetPointVelocity(part.transform.position), part.transform.position - transform.position);
                    part.gameObject.SetActive(false);
                }
                initializedFromHost = true;
            }
            finally { Restoring = false; }
        }
    }
}
