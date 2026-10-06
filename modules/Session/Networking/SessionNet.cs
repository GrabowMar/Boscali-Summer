using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Modules.Session.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Session.Networking
{
    /// <summary>A client with the mod says hello once it is in the mission, naming its version.</summary>
    [NetworkMessage]
    internal struct SessionHello
    {
        public byte Protocol;
        public string Version;
    }

    /// <summary>Host-owned settings as serialized text, in parallel arrays.</summary>
    [NetworkMessage]
    internal struct HostSettingsMessage
    {
        public byte Protocol;
        public string Version;
        public byte Flags;
        public string[] Keys;
        public string[] Values;
    }

    /// <summary>
    /// The session handshake. A client that joins says hello; the host answers with its mod
    /// version and every host-owned setting (<see cref="HostAuthority"/>), then pushes each
    /// change it makes from the SET SERVER page or the config window. The client runs the
    /// session on the host's values and puts its own back when it leaves.
    ///
    /// <para>The client's config file is never written with the host's values: while a
    /// session holds any, saving on set is off and the host-owned entries are read-only in the
    /// F1 window. Leaving the session restores the player's values and saves once, which also
    /// keeps any presentation change made during the session. A crash loses only those
    /// in-session presentation changes; the file still holds the player's own values.</para>
    ///
    /// <para>A player without the mod never says hello, so the host never sends it anything.
    /// A host without the mod never answers, and after a few tries the client says so once on
    /// the HUD instead of leaving every panel waiting in silence.</para>
    /// </summary>
    internal sealed class SessionNet : MonoBehaviour
    {
        internal const byte ProtocolVersion = 1;
        /// <summary>The message answers this client's hello.</summary>
        internal const byte FlagReply = 1;
        /// <summary>Last message of a full snapshot.</summary>
        internal const byte FlagComplete = 2;

        private const string HudChannel = "session";
        private const float RegistrationInterval = 0.5f;
        private const float HelloInterval = 4f;
        private const int HelloAttempts = 4;
        private const float HelloSpacing = 2f;
        private const float FlushInterval = 0.5f;
        private const int MaximumPeers = 64;
        private const int MaximumVersionLength = 32;

        private readonly Dictionary<string, ConfigEntryBase> allowed =
            new Dictionary<string, ConfigEntryBase>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConfigEntryBase> switches =
            new Dictionary<string, ConfigEntryBase>(StringComparer.Ordinal);
        private readonly List<string> switchMismatches = new List<string>();
        private readonly HashSet<ConfigEntryBase> owned = new HashSet<ConfigEntryBase>();
        private readonly HashSet<ConfigEntryBase> dirty = new HashSet<ConfigEntryBase>();
        private readonly Dictionary<INetworkPlayer, float> peers = new Dictionary<INetworkPlayer, float>();
        private readonly List<INetworkPlayer> dropped = new List<INetworkPlayer>();
        private readonly List<KeyValuePair<string, string>> outgoing = new List<KeyValuePair<string, string>>();
        private readonly HostValueLedger ledger = new HostValueLedger();
        private ConfigEntryBase[] entries = Array.Empty<ConfigEntryBase>();
        private ConfigEntryBase[] switchEntries = Array.Empty<ConfigEntryBase>();
        private Func<string, string> readLocal;
        private Action<string, string> writeLocal;

        private ConfigFile config;
        private HandlerSlot serverSlot, clientSlot;
        private HandlerSlot ServerHandlers => serverSlot ??= HandlerSlot.Of<SessionHello>(ReceiveHello);
        private HandlerSlot ClientHandlers => clientSlot ??= HandlerSlot.Of<HostSettingsMessage>(ReceiveSettings);
        private float nextRegistration;
        private float nextHello;
        private float nextFlush;
        private int helloAttempts;
        private bool answered;
        private bool hostSilentShown;
        private bool versionShown;
        private bool savedSaveOnSet;
        private bool applying;
        private static bool serializersInstalled;

        public void Configure(ConfigEntryBase[] hostOwned, ConfigEntryBase[] moduleSwitches)
        {
            entries = hostOwned ?? Array.Empty<ConfigEntryBase>();
            switchEntries = moduleSwitches ?? Array.Empty<ConfigEntryBase>();
            for (int i = 0; i < switchEntries.Length; i++)
                if (switchEntries[i] != null) switches[HostAuthority.Key(switchEntries[i])] = switchEntries[i];
            for (int i = 0; i < entries.Length; i++)
            {
                ConfigEntryBase entry = entries[i];
                if (entry == null) continue;
                config = config ?? entry.ConfigFile;
                allowed[HostAuthority.Key(entry)] = entry;
                owned.Add(entry);
            }
            readLocal = ReadLocal;
            writeLocal = WriteLocal;
            if (config != null) config.SettingChanged += OnSettingChanged;
            InstallSerializers();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextRegistration)
            {
                nextRegistration = now + RegistrationInterval;
                Register();
            }

            bool server = GameAccess.IsServer();
            if (server)
            {
                // This peer became the host (or never left it): its own values are the ones that count.
                if (ledger.Active) EndSession("now hosting");
                if (dirty.Count > 0 && now >= nextFlush)
                {
                    nextFlush = now + FlushInterval;
                    PushChanges();
                }
                return;
            }

            NetworkClient client = ClientOrNull();
            if (client == null || !client.Active)
            {
                if (ledger.Active) EndSession("left the server");
                ResetHandshake();
                return;
            }
            if (answered || now < nextHello || !GameManager.GetLocalPlayer<Player>(out Player local) || local == null)
                return;
            if (helloAttempts >= HelloAttempts)
            {
                if (!hostSilentShown)
                {
                    hostSilentShown = true;
                    Plugin.Logger?.LogWarning("[Session] The host did not answer the session handshake; it runs an older " +
                        "Boscali Summer or none. Host-owned settings stay at this client's values.");
                    Notice(HudTone.Caution, "HOST DID NOT ANSWER",
                        "The host runs an older Boscali Summer or none; host-run panels may stay empty.");
                }
                return;
            }
            helloAttempts++;
            nextHello = now + HelloInterval;
            client.Send(new SessionHello { Protocol = ProtocolVersion, Version = Plugin.PluginVersion });
        }

        private void Register()
        {
            NetworkManagerNuclearOption network;
            try { network = NetworkManagerNuclearOption.i; }
            catch { return; }

            MessageHandler server = network?.Server != null && network.Server.Active ? network.Server.MessageHandler : null;
            if (ServerHandlers.Swap(server)) peers.Clear();

            MessageHandler client = network?.Client?.MessageHandler;
            if (ClientHandlers.Swap(client))
            {
                // A new connection is a new session: forget the last host and say hello again.
                if (ledger.Active) EndSession("changed server");
                ResetHandshake();
            }
        }

        private void ResetHandshake()
        {
            answered = false;
            helloAttempts = 0;
            hostSilentShown = false;
            versionShown = false;
            switchMismatches.Clear();
            nextHello = Time.unscaledTime + 1f;
        }

        private static NetworkClient ClientOrNull()
        {
            try { return NetworkManagerNuclearOption.i?.Client; }
            catch { return null; }
        }

        // ---- Host ----------------------------------------------------------------------------

        private void ReceiveHello(INetworkPlayer sender, SessionHello hello)
        {
            if (!GameAccess.IsServer() || hello.Protocol != ProtocolVersion || sender == null || !sender.IsAuthenticated)
                return;
            float now = Time.unscaledTime;
            if (peers.TryGetValue(sender, out float last) && now - last < HelloSpacing) return;
            if (!peers.ContainsKey(sender) && peers.Count >= MaximumPeers) EvictOldestPeer();
            peers[sender] = now;

            if (!string.Equals(hello.Version, Plugin.PluginVersion, StringComparison.Ordinal))
                Plugin.Logger?.LogWarning("[Session] A client joined with Boscali Summer " + (hello.Version ?? "?") +
                    "; this host runs " + Plugin.PluginVersion + ". Mismatched versions can break panels.");

            outgoing.Clear();
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null) outgoing.Add(new KeyValuePair<string, string>(HostAuthority.Key(entries[i]), entries[i].GetSerializedValue()));
            // The switches ride along for comparison only: a client never applies them.
            for (int i = 0; i < switchEntries.Length; i++)
                if (switchEntries[i] != null) outgoing.Add(new KeyValuePair<string, string>(HostAuthority.Key(switchEntries[i]), switchEntries[i].GetSerializedValue()));
            List<KeyValuePair<string[], string[]>> chunks = HostValueChunks.Split(outgoing);
            for (int i = 0; i < chunks.Count; i++)
            {
                byte flags = (byte)(FlagReply | (i == chunks.Count - 1 ? FlagComplete : 0));
                if (!TrySend(sender, Message(flags, chunks[i]))) break;
            }
        }

        /// <summary>
        /// Nothing tells this component a peer left, so a full roster makes room by dropping
        /// whoever said hello longest ago; a peer still connected is re-added by its next hello.
        /// </summary>
        private void EvictOldestPeer()
        {
            INetworkPlayer oldest = null;
            float oldestTime = float.MaxValue;
            foreach (KeyValuePair<INetworkPlayer, float> peer in peers)
            {
                if (peer.Key == null || !peer.Key.IsAuthenticated)
                {
                    oldest = peer.Key;
                    break;
                }
                if (peer.Value >= oldestTime) continue;
                oldest = peer.Key;
                oldestTime = peer.Value;
            }
            if (oldest != null) peers.Remove(oldest);
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs change)
        {
            // Presentation writers save often in flight, so the filter is a set lookup, not a key build.
            if (applying || change?.ChangedSetting == null || !owned.Contains(change.ChangedSetting) ||
                !GameAccess.IsServer()) return;
            dirty.Add(change.ChangedSetting);
        }

        private void PushChanges()
        {
            outgoing.Clear();
            foreach (ConfigEntryBase entry in dirty)
                outgoing.Add(new KeyValuePair<string, string>(HostAuthority.Key(entry), entry.GetSerializedValue()));
            dirty.Clear();
            if (peers.Count == 0) return;

            List<KeyValuePair<string[], string[]>> chunks = HostValueChunks.Split(outgoing);
            dropped.Clear();
            foreach (KeyValuePair<INetworkPlayer, float> peer in peers)
            {
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (TrySend(peer.Key, Message(0, chunks[i]))) continue;
                    dropped.Add(peer.Key);
                    break;
                }
            }
            for (int i = 0; i < dropped.Count; i++) peers.Remove(dropped[i]);
        }

        private static HostSettingsMessage Message(byte flags, KeyValuePair<string[], string[]> chunk) =>
            new HostSettingsMessage
            {
                Protocol = ProtocolVersion,
                Version = Plugin.PluginVersion,
                Flags = flags,
                Keys = chunk.Key,
                Values = chunk.Value
            };

        private static bool TrySend(INetworkPlayer peer, HostSettingsMessage message)
        {
            if (peer == null || !peer.IsAuthenticated) return false;
            try
            {
                peer.Send(message);
                return true;
            }
            catch (Exception)
            {
                // A peer that left between frames: forget it rather than throw every send.
                return false;
            }
        }

        // ---- Client --------------------------------------------------------------------------

        private void ReceiveSettings(INetworkPlayer _, HostSettingsMessage message)
        {
            if (GameAccess.IsServer() || message.Protocol != ProtocolVersion || config == null ||
                message.Keys == null || message.Values == null || message.Keys.Length != message.Values.Length ||
                message.Keys.Length > HostValueChunks.MaximumEntries)
                return;

            if ((message.Flags & FlagReply) != 0)
            {
                answered = true;
                if (!versionShown && !string.Equals(message.Version, Plugin.PluginVersion, StringComparison.Ordinal))
                {
                    versionShown = true;
                    string host = string.IsNullOrEmpty(message.Version) ? "?" : message.Version;
                    Plugin.Logger?.LogWarning("[Session] Host runs Boscali Summer " + host + "; this client runs " +
                        Plugin.PluginVersion + ".");
                    Notice(HudTone.Caution, "BOSCALI SUMMER " + Plugin.PluginVersion + " · HOST " + host,
                        "Install the host's version so every panel matches.");
                }
            }

            if (!ledger.Active) BeginSession();
            int changed = 0;
            applying = true;
            try
            {
                for (int i = 0; i < message.Keys.Length; i++)
                {
                    if (!HostValueChunks.Fits(message.Keys[i], message.Values[i])) continue;
                    if (switches.TryGetValue(message.Keys[i], out ConfigEntryBase own))
                    {
                        if (!string.Equals(own.GetSerializedValue(), message.Values[i], StringComparison.OrdinalIgnoreCase))
                            switchMismatches.Add(own.Definition.Section.ToUpperInvariant() + " " +
                                (string.Equals(message.Values[i], "true", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF"));
                        continue;
                    }
                    if (ledger.Apply(message.Keys[i], message.Values[i], readLocal, writeLocal)) changed++;
                }
            }
            finally
            {
                applying = false;
            }
            if (changed > 0) Plugin.Logger?.LogInfo("[Session] Using " + changed + " host setting(s) for this session.");
            if ((message.Flags & (FlagReply | FlagComplete)) == (FlagReply | FlagComplete) && switchMismatches.Count > 0)
            {
                string list = string.Join(", ", switchMismatches);
                switchMismatches.Clear();
                Plugin.Logger?.LogWarning("[Session] The host's module switches differ from this client's: host has " + list +
                    ". Match them in the config and restart the game.");
                Notice(HudTone.Caution, "HOST MODULES: " + list, "Match the host's module switches and restart.");
            }
        }

        private void BeginSession()
        {
            savedSaveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            ConfigMenu.SetReadOnly(entries, true);
        }

        private void EndSession(string reason)
        {
            if (!ledger.Active || config == null) return;
            applying = true;
            try
            {
                ledger.Restore(writeLocal);
            }
            finally
            {
                applying = false;
            }
            ConfigMenu.SetReadOnly(entries, false);
            config.SaveOnConfigSet = savedSaveOnSet;
            try { config.Save(); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Session] Could not save settings: " + e.Message); }
            Plugin.Logger?.LogInfo("[Session] Restored your own settings (" + reason + ").");
        }

        private string ReadLocal(string key) =>
            allowed.TryGetValue(key, out ConfigEntryBase entry) ? entry.GetSerializedValue() : null;

        private void WriteLocal(string key, string value)
        {
            if (!allowed.TryGetValue(key, out ConfigEntryBase entry)) return;
            try { entry.SetSerializedValue(value); }
            catch (Exception e) { Plugin.Logger?.LogWarning("[Session] Ignored host value for " + key + ": " + e.Message); }
        }

        private static void Notice(HudTone tone, string text, string detail)
        {
            if (!ModuleServices.TryGet(out IHudBoard board)) return;
            board.DeclareChannel(HudChannel, "SESSION");
            board.Notice(HudChannel, tone, text, detail);
        }

        private void OnApplicationQuit() => EndSession("quitting");

        private void OnDestroy()
        {
            EndSession("unloading");
            if (config != null) config.SettingChanged -= OnSettingChanged;
            ServerHandlers.Release();
            ClientHandlers.Release();
            peers.Clear();
        }

        // ---- Wire ----------------------------------------------------------------------------

        private static void InstallSerializers()
        {
            if (serializersInstalled) return;
            serializersInstalled = true;

            MirageSerializers.Strict.Install<SessionHello>((Action<NetworkWriter, SessionHello>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteString(Bounded(v.Version, MaximumVersionLength));
            }),
            (Func<NetworkReader, SessionHello>)(r =>
            {
                byte protocol = r.ReadByte();
                if (protocol != ProtocolVersion) return new SessionHello { Protocol = protocol };
                return new SessionHello { Protocol = protocol, Version = Bounded(r.ReadString(), MaximumVersionLength) };
            }));

            MirageSerializers.Strict.Install<HostSettingsMessage>((Action<NetworkWriter, HostSettingsMessage>)((w, v) =>
            {
                w.WriteByte(v.Protocol);
                w.WriteString(Bounded(v.Version, MaximumVersionLength));
                w.WriteByte(v.Flags);
                int count = v.Keys == null || v.Values == null ? 0 : Math.Min(v.Keys.Length, v.Values.Length);
                if (count > HostValueChunks.MaximumEntries) count = HostValueChunks.MaximumEntries;
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteString(v.Keys[i]);
                    w.WriteString(v.Values[i]);
                }
            }),
            (Func<NetworkReader, HostSettingsMessage>)(r =>
            {
                byte protocol = r.ReadByte();
                var message = new HostSettingsMessage { Protocol = protocol };
                if (protocol != ProtocolVersion) return message;
                message.Version = Bounded(r.ReadString(), MaximumVersionLength);
                message.Flags = r.ReadByte();
                int count = r.ReadByte();
                // An oversized or broken message is dropped whole; the handler ignores null arrays.
                if (count > HostValueChunks.MaximumEntries) return message;
                var keys = new string[count];
                var values = new string[count];
                for (int i = 0; i < count; i++)
                {
                    keys[i] = r.ReadString();
                    values[i] = r.ReadString();
                }
                message.Keys = keys;
                message.Values = values;
                return message;
            }));

        }

        private static string Bounded(string value, int length) =>
            value == null ? "" : value.Length <= length ? value : value.Substring(0, length);
    }
}
