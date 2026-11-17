using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct Sa74State
    {
        internal byte Protocol;
        internal uint Epoch, Id, Owner;
        internal Vector3 Position;
        internal float Age;
        internal int Events;
    }

    /// <summary>Server-to-client aftermath snapshots, including scars for late joiners. No client detonation intent exists.</summary>
    internal sealed class Sa74Net : MonoBehaviour, ISceneService
    {
        private const byte ProtocolVersion = 1;
        private HandlerSlot slot;
        private float nextRegistration, nextSnapshot;
        private HandlerSlot Handlers => slot ??= HandlerSlot.Of<Sa74State>(Receive);
        private void Awake()
        {
            MirageSerializers.Strict.Install<Sa74State>(
                (Action<NetworkWriter, Sa74State>)((w, v) =>
                {
                    w.WriteByte(v.Protocol); w.WritePackedUInt32(v.Epoch); w.WritePackedUInt32(v.Id);
                    w.WritePackedUInt32(v.Owner); w.WriteVector3(v.Position); w.WriteSingle(v.Age); w.WriteInt32(v.Events);
                }),
                (Func<NetworkReader, Sa74State>)(r =>
                {
                    byte protocol = r.ReadByte();
                    return protocol != ProtocolVersion ? new Sa74State { Protocol = protocol } : new Sa74State
                    {
                        Protocol = protocol, Epoch = r.ReadPackedUInt32(), Id = r.ReadPackedUInt32(), Owner = r.ReadPackedUInt32(),
                        Position = r.ReadVector3(), Age = r.ReadSingle(), Events = r.ReadInt32()
                    };
                }));
        }
        public void ResetForScene() { nextRegistration = nextSnapshot = 0f; }
        private void Update()
        {
            NetFeed.Poll(Handlers, ref nextRegistration);
            if (!GameAccess.IsServer() || Time.unscaledTime < nextSnapshot) return;
            nextSnapshot = Time.unscaledTime + 3f;
            if (!Sa74Fallout.Active)
                NetFeed.Broadcast(new Sa74State { Protocol = ProtocolVersion, Epoch = Sa74Fallout.Epoch });
            foreach (Sa74Fallout.Site site in Sa74Fallout.Sites) Broadcast(site);
        }
        internal static void Broadcast(Sa74Fallout.Site site) => NetFeed.Broadcast(new Sa74State
        {
            Protocol = ProtocolVersion, Epoch = Sa74Fallout.Epoch, Id = site.Id, Owner = site.Owner,
            Position = site.Position.AsVector3(), Age = Mathf.Max(0f, Time.time - site.Started), Events = Sa74Fallout.Events
        });
        private static void Receive(INetworkPlayer sender, Sa74State state)
        {
            if (GameAccess.IsServer() || state.Protocol != ProtocolVersion ||
                float.IsNaN(state.Age) || float.IsInfinity(state.Age)) return;
            if (state.Id == 0 && state.Events == 0) { Sa74Fallout.ReceiveReset(state.Epoch); return; }
            if (state.Events < 1) return;
            Sa74Fallout.Receive(state.Epoch, state.Id, state.Position, state.Age, state.Owner, state.Events);
        }
        private void OnDestroy() => Handlers.Release();
    }
}
