using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    // Global map coordinates survive a floating-origin shift. The survivor mask is captured before native despawn.
    [NetworkMessage]
    internal struct MegaplaneCrashRecord
    {
        internal const int MaxFields = 4;
        internal const float Lifetime = 600f;
        internal uint Spawn, Unit;
        internal Vector3 Origin, Contact, Velocity, Normal;
        internal Quaternion Rotation;
        internal ulong Surviving;
        internal bool Water;
        internal byte GearLeg, GearDoor;
        internal float Age;

        private static void Vector(NetworkWriter w, Vector3 v)
        {
            w.WriteSingle(v.x); w.WriteSingle(v.y); w.WriteSingle(v.z);
        }

        private static Vector3 Vector(NetworkReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        internal static void Write(NetworkWriter w, MegaplaneCrashRecord v)
        {
            w.WriteByte(MegaplaneWire.ProtocolVersion);
            w.WritePackedUInt32(v.Spawn); w.WritePackedUInt32(v.Unit);
            Vector(w, v.Origin); Vector(w, v.Contact); Vector(w, v.Velocity); Vector(w, v.Normal);
            w.WriteSingle(v.Rotation.x); w.WriteSingle(v.Rotation.y); w.WriteSingle(v.Rotation.z); w.WriteSingle(v.Rotation.w);
            w.WriteUInt64(v.Surviving); w.WriteBoolean(v.Water);
            w.WriteByte(v.GearLeg); w.WriteByte(v.GearDoor); w.WriteSingle(v.Age);
        }

        internal static MegaplaneCrashRecord Read(NetworkReader r)
        {
            if (r.ReadByte() != MegaplaneWire.ProtocolVersion) return default;
            var v = new MegaplaneCrashRecord
            {
                Spawn = r.ReadPackedUInt32(), Unit = r.ReadPackedUInt32(),
                Origin = Vector(r), Contact = Vector(r), Velocity = Vector(r), Normal = Vector(r),
                Rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                Surviving = r.ReadUInt64(), Water = r.ReadBoolean(),
                GearLeg = r.ReadByte(), GearDoor = r.ReadByte(), Age = r.ReadSingle()
            };
            // Positive bounds also reject NaN. A wreck must be close to its recorded airframe on the map.
            float orientation = Quaternion.Dot(v.Rotation, v.Rotation);
            if (!(v.Contact.sqrMagnitude < 1e14f && (v.Origin - v.Contact).sqrMagnitude < 1e6f &&
                  v.Velocity.sqrMagnitude < 1e8f && v.Normal.sqrMagnitude > .5f && v.Normal.sqrMagnitude < 1.5f &&
                  orientation > .8f && orientation < 1.2f && v.Age >= 0f && v.Age < Lifetime)) return default;
            return v;
        }
    }

    internal sealed partial class MegaplaneNet
    {
        private HandlerSlot crashSlot;
        private HandlerSlot CrashHandler => crashSlot ??= HandlerSlot.Of<MegaplaneCrashRecord>(ReceiveCrash);
        private readonly List<(MegaplaneCrashRecord record, float born)> crashes = new List<(MegaplaneCrashRecord, float)>();
        private Mission crashMission;
        private Transform crashOrigin;
        private float nextCrashRefresh;

        private void ResetCrashes()
        {
            crashes.Clear();
            MegaplaneWreck.Clear();
        }

        private void CheckCrashWorld()
        {
            if (!ReferenceEquals(crashMission, MissionManager.CurrentMission) || !ReferenceEquals(crashOrigin, Datum.origin))
            {
                ResetCrashes();
                crashMission = MissionManager.CurrentMission;
                crashOrigin = Datum.origin;
            }
            float now = Time.time;
            for (int i = crashes.Count - 1; i >= 0; i--)
                if (now - crashes[i].born >= MegaplaneCrashRecord.Lifetime) crashes.RemoveAt(i);
        }

        private bool ShowCrash(MegaplaneCrashRecord record)
        {
            CheckCrashWorld();
            if (record.Spawn == 0 || crashOrigin == null || MegaplaneTemplate.Definition == null) return false;
            foreach (var existing in crashes)
                if (existing.record.Spawn == record.Spawn && existing.record.Unit == record.Unit) return false;
            float born = Time.time - record.Age;
            // A newcomer can receive a live impact before the host refreshes older fields.
            // Retain the same four youngest wrecks on both ends regardless of arrival order.
            if (crashes.Count == MegaplaneCrashRecord.MaxFields)
            {
                if (born <= crashes[0].born) return false;
                crashes.RemoveAt(0);
            }
            MegaplaneWreck.Show(record);
            int index = crashes.Count;
            while (index > 0 && crashes[index - 1].born > born) index--;
            crashes.Insert(index, (record, born));
            return true;
        }

        internal static void Crash(MegaplaneCrashRecord record)
        {
            if (!GameAccess.IsServer()) return;
            Ensure();
            if (Instance.ShowCrash(record)) SendCrash(GameAccess.NetworkManagerOrNull?.Server, record);
        }

        private void ReceiveCrash(INetworkPlayer _, MegaplaneCrashRecord record)
        {
            if (!GameAccess.IsServer() && record.Spawn != 0) ShowCrash(record);
        }

        private static void SendCrash(NetworkServer server, MegaplaneCrashRecord record)
        {
            if (server == null || !server.Active) return;
            foreach (INetworkPlayer peer in server.AuthenticatedPlayers)
                if (peer != null && peer.IsAuthenticated)
                    try { peer.Send(record); }
                    catch (Exception) { /* Peer disconnected during the broadcast. */ }
        }

        private void UpdateCrashes(NetworkManagerNuclearOption network)
        {
            CheckCrashWorld();
            if (crashes.Count == 0 || !GameAccess.IsServer() || Time.unscaledTime < nextCrashRefresh) return;
            nextCrashRefresh = Time.unscaledTime + 5f;
            foreach (var entry in crashes)
            {
                var record = entry.record;
                record.Age = Time.time - entry.born;
                SendCrash(network?.Server, record);
            }
        }
    }
}
