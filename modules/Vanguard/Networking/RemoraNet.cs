using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Networking
{
    [NetworkMessage]
    internal struct RemoraState
    {
        public byte Protocol;
        public uint Drone, Launcher;
        public byte Fired, Phase, Flares, Jams, Shots;
    }

    /// <summary>Server-to-client REMORA presentation (empty rails, flares, launcher HUD). Sent on change, resent every 2 s.</summary>
    internal sealed class RemoraNet : MonoBehaviour, ISceneService
    {
        internal const byte ProtocolVersion = 4; // 3: ECM bursts left (Jams); 4: gun rounds fired (Shots)
        private const float ResendInterval = 2f;
        private HandlerSlot slot;
        private HandlerSlot ClientHandlers => slot ??= HandlerSlot.Of<RemoraState>(Receive);
        private float nextRegistration;
        private float nextResend;

        private void Awake()
        {
            MirageSerializers.Strict.Install<RemoraState>(
                (Action<NetworkWriter, RemoraState>)((w, v) =>
                {
                    w.WriteByte(v.Protocol);
                    w.WritePackedUInt32(v.Drone);
                    w.WritePackedUInt32(v.Launcher);
                    w.WriteByte(v.Fired);
                    w.WriteByte(v.Phase);
                    w.WriteByte(v.Flares);
                    w.WriteByte(v.Jams);
                    w.WriteByte(v.Shots);
                }),
                (Func<NetworkReader, RemoraState>)(r =>
                {
                    byte protocol = r.ReadByte();
                    if (protocol != ProtocolVersion) return new RemoraState { Protocol = protocol };
                    return new RemoraState
                    {
                        Protocol = protocol, Drone = r.ReadPackedUInt32(), Launcher = r.ReadPackedUInt32(),
                        Fired = r.ReadByte(), Phase = r.ReadByte(), Flares = r.ReadByte(), Jams = r.ReadByte(), Shots = r.ReadByte(),
                    };
                }));
            RemoraBoard.Changed = Broadcast;
        }

        public void ResetForScene() => RemoraBoard.Views.Clear();

        private void Update()
        {
            if (Time.unscaledTime >= nextResend && GameAccess.IsServer())
            {
                nextResend = Time.unscaledTime + ResendInterval;
                foreach (RemoraView view in RemoraBoard.Views.Values) Broadcast(view);
            }
            NetFeed.Poll(ClientHandlers, ref nextRegistration);
        }

        private static void Broadcast(RemoraView view)
        {
            NetFeed.Broadcast(new RemoraState
            {
                Protocol = ProtocolVersion, Drone = view.Drone, Launcher = view.Launcher,
                Fired = view.Fired, Phase = (byte)view.Phase, Flares = view.Flares, Jams = view.Jams, Shots = view.Shots,
            });
        }

        private void Receive(INetworkPlayer sender, RemoraState state)
        {
            // Fired is a 2-bit mask for a REMORA and a 4-bit one for a MANTA (Domain/DroneRounds).
            if (state.Protocol != ProtocolVersion || GameAccess.IsServer() || state.Fired > 15 || state.Phase > (byte)RemoraPhase.Ram ||
                state.Flares > MantaRules.FlareBursts || state.Jams > RemoraArcher.JamBursts) return;
            RemoraBoard.Views[state.Drone] = new RemoraView
            {
                Drone = state.Drone, Launcher = state.Launcher, Fired = state.Fired, Phase = (RemoraPhase)state.Phase,
                Flares = state.Flares,
                Jams = state.Jams,
                Shots = state.Shots,
            };
        }

        private void OnDestroy()
        {
            ClientHandlers.Release();
            if (RemoraBoard.Changed == (Action<RemoraView>)Broadcast) RemoraBoard.Changed = null;
        }
    }
}
