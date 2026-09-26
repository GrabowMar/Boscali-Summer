using System;
using System.Reflection;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Runtime;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Networking
{
    /// <summary>
    /// The host's weather key: the only weather data on the wire. Mirage derives the message id
    /// from this type's full name, so never rename or move it without bumping every peer.
    /// </summary>
    [NetworkMessage]
    internal struct WeatherKeyMessage
    {
        public byte Protocol;
        public uint Seed;
        public float Epoch;
        public bool Dynamic;
        public byte StartRegime;
        public byte Flags;
        public byte OverrideCount;
        public float Override0Time, Override1Time, Override2Time, Override3Time;
        public byte Override0Regime, Override1Regime, Override2Regime, Override3Regime;
    }

    /// <summary>
    /// Sends the key host → clients and applies it on clients. The host re-sends every
    /// <see cref="ResendInterval"/> as well as on change: a 40-byte message is cheaper than
    /// tracking joins, and a late joiner is correct within seconds (and usually immediately,
    /// because its locally derived default key already matches the host's).
    /// </summary>
    internal sealed class WeatherNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        private const float ResendInterval = 2f;

        private Action<WeatherKey> onRemoteKey;
        private MessageHandler clientHandler;
        private WeatherKey lastSent;
        private float nextRegistration;
        private float nextResend;

        public void Configure(Action<WeatherKey> remoteKeyHandler)
        {
            onRemoteKey = remoteKeyHandler;
            InstallSerializers();
        }

        public void ResetScene()
        {
            clientHandler?.UnregisterHandler<WeatherKeyMessage>();
            clientHandler = null;
            lastSent = null;
            nextRegistration = 0f;
            nextResend = 0f;
        }

        /// <summary>Host: publish the key now if it changed, and periodically regardless.</summary>
        public void Publish(WeatherKey key)
        {
            if (key == null || !GameAccess.IsServer()) return;
            bool changed = !key.Equals(lastSent);
            if (!changed && Time.unscaledTime < nextResend) return;

            NetworkServer server = NetworkManagerNuclearOption.i?.Server;
            if (server == null || !server.Active) return;
            nextResend = Time.unscaledTime + ResendInterval;
            lastSent = key;
            server.SendToAll(ToMessage(key), authenticatedOnly: true, excludeLocalPlayer: true);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRegistration) return;
            nextRegistration = Time.unscaledTime + 0.5f;
            MessageHandler client = NetworkManagerNuclearOption.i?.Client?.MessageHandler;
            if (client == clientHandler) return;
            clientHandler?.UnregisterHandler<WeatherKeyMessage>();
            clientHandler = client;
            clientHandler?.RegisterHandler<WeatherKeyMessage>(Receive, false);
        }

        private void Receive(INetworkPlayer _, WeatherKeyMessage message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion) return;
            onRemoteKey?.Invoke(FromMessage(message));
        }

        private void OnDestroy()
        {
            clientHandler?.UnregisterHandler<WeatherKeyMessage>();
        }

        internal static WeatherKeyMessage ToMessage(WeatherKey key)
        {
            var m = new WeatherKeyMessage
            {
                Protocol = ProtocolVersion,
                Seed = key.Seed,
                Epoch = key.Epoch,
                Dynamic = key.Dynamic,
                StartRegime = key.StartRegime,
                Flags = (byte)key.Flags,
                OverrideCount = (byte)Math.Min(key.OverrideCount, WeatherKey.MaxOverrides),
            };
            for (int i = 0; i < m.OverrideCount; i++)
            {
                RegimeOverride o = key.Override(i);
                switch (i)
                {
                    case 0: m.Override0Time = o.Time; m.Override0Regime = (byte)o.Regime; break;
                    case 1: m.Override1Time = o.Time; m.Override1Regime = (byte)o.Regime; break;
                    case 2: m.Override2Time = o.Time; m.Override2Regime = (byte)o.Regime; break;
                    default: m.Override3Time = o.Time; m.Override3Regime = (byte)o.Regime; break;
                }
            }
            return m;
        }

        internal static WeatherKey FromMessage(WeatherKeyMessage m)
        {
            int count = Math.Min(m.OverrideCount, (byte)WeatherKey.MaxOverrides);
            var overrides = new RegimeOverride[count];
            for (int i = 0; i < count; i++)
            {
                switch (i)
                {
                    case 0: overrides[i] = new RegimeOverride(m.Override0Time, RegimeTable.Clamp(m.Override0Regime)); break;
                    case 1: overrides[i] = new RegimeOverride(m.Override1Time, RegimeTable.Clamp(m.Override1Regime)); break;
                    case 2: overrides[i] = new RegimeOverride(m.Override2Time, RegimeTable.Clamp(m.Override2Regime)); break;
                    default: overrides[i] = new RegimeOverride(m.Override3Time, RegimeTable.Clamp(m.Override3Regime)); break;
                }
            }
            return new WeatherKey(m.Seed, m.Epoch, m.Dynamic, m.StartRegime, (WeatherFlags)m.Flags & WeatherFlags.All, overrides);
        }

        private static void InstallSerializers()
        {
            Bind(typeof(Writer<WeatherKeyMessage>), "Write", (Action<NetworkWriter, WeatherKeyMessage>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WritePackedUInt32(v.Seed);
                w.WriteSingle(v.Epoch);
                w.WriteByte((byte)((v.Dynamic ? 1 : 0)));
                w.WriteByte(v.StartRegime);
                w.WriteByte(v.Flags);
                byte count = Math.Min(v.OverrideCount, (byte)WeatherKey.MaxOverrides);
                w.WriteByte(count);
                if (count > 0) { w.WriteSingle(v.Override0Time); w.WriteByte(v.Override0Regime); }
                if (count > 1) { w.WriteSingle(v.Override1Time); w.WriteByte(v.Override1Regime); }
                if (count > 2) { w.WriteSingle(v.Override2Time); w.WriteByte(v.Override2Regime); }
                if (count > 3) { w.WriteSingle(v.Override3Time); w.WriteByte(v.Override3Regime); }
            }));
            Bind(typeof(Reader<WeatherKeyMessage>), "Read", (Func<NetworkReader, WeatherKeyMessage>)(r =>
            {
                byte protocol = r.ReadByte();
                var m = new WeatherKeyMessage { Protocol = protocol };
                if (protocol != ProtocolVersion) return m;
                m.Seed = r.ReadPackedUInt32();
                m.Epoch = r.ReadSingle();
                m.Dynamic = r.ReadByte() != 0;
                m.StartRegime = r.ReadByte();
                m.Flags = r.ReadByte();
                m.OverrideCount = Math.Min(r.ReadByte(), (byte)WeatherKey.MaxOverrides);
                if (m.OverrideCount > 0) { m.Override0Time = r.ReadSingle(); m.Override0Regime = r.ReadByte(); }
                if (m.OverrideCount > 1) { m.Override1Time = r.ReadSingle(); m.Override1Regime = r.ReadByte(); }
                if (m.OverrideCount > 2) { m.Override2Time = r.ReadSingle(); m.Override2Regime = r.ReadByte(); }
                if (m.OverrideCount > 3) { m.Override3Time = r.ReadSingle(); m.Override3Regime = r.ReadByte(); }
                return m;
            }));
            MessagePacker.RegisterMessage<WeatherKeyMessage>();
        }

        private static void Bind(Type holder, string property, object value)
        {
            PropertyInfo target = holder.GetProperty(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null)
            {
                Plugin.Logger.LogError("[Weather] Mirage serializer seam " + holder.Name + "." + property +
                    " is missing; the weather key cannot replicate on this game build.");
                return;
            }
            target.SetValue(null, value, null);
        }
    }
}
