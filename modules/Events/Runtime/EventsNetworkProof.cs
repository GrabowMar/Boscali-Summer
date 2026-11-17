#if !PUBLIC_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Net;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Networking;
using Mirage;
using Mirage.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Runtime
{
    /// <summary>Real-peer regression levers. Never available in player builds or outside the kit's loopback peers.</summary>
    public static class EventsNetworkProof
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")) ||
                GameManager.gameState != GameState.Multiplayer)
                throw new InvalidOperationException("Events network proof requires real loopback peers.");
            EventsManager manager = Find<EventsManager>();
            EventsNet network = Find<EventsNet>();
            if (manager == null || network == null) throw new InvalidOperationException("Events services are required.");
            EventsSettings settings = Field<EventsSettings>(manager, "settings");
            string action = BoscaliSummer.Core.Diagnostics.AutomationArgs.Text(args, "action") ?? "read";
            if (action == "reset_client")
            {
                if (GameAccess.IsServer()) throw new InvalidOperationException("Reset the remote client's mirror only.");
                settings.Enabled.Value = false; // Presentation off must still mirror host prices.
                manager.ResetForScene();
            }
            else if (action == "publish_host")
            {
                if (!GameAccess.IsServer() || !MissionTimeSource.TryNow(out float now))
                    throw new InvalidOperationException("A running authoritative mission is required.");
                int index = Array.FindIndex(EventCatalog.All, e => e.Tier == EventTier.Minor &&
                    e.Target == EventTarget.All && Math.Abs(e.SupportCostMultiplier - 1f) > 0.01f);
                if (index < 0) throw new InvalidOperationException("A global minor price event is required.");
                float end = now + 120f;
                manager.ApplyRemote((sbyte)index, 0, now, end, settings.EffectStrength.Value);
                // This is one broadcast, not a heartbeat recovery. Leave the ordinary 15 s heartbeat in the future.
                typeof(EventsManager).GetField("nextHeartbeat", Fields).SetValue(manager, now + 15f);
                network.Broadcast((sbyte)index, 0, now, end, settings.EffectStrength.Value);
            }
            else if (action != "read") throw new InvalidOperationException("Unknown Events network proof action.");

            NetworkClient client = GameAccess.NetworkManagerOrNull?.Client;
            HandlerSlot slot = Field<HandlerSlot>(network, "clientSlot");
            MessageHandler handler = client?.MessageHandler;
            IDictionary handlers = handler == null ? null : (IDictionary)typeof(MessageHandler)
                .GetField("_messageHandlers", Fields).GetValue(handler);
            return JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["ok"] = true, ["server"] = GameAccess.IsServer(),
                ["authenticated"] = client?.Player?.IsAuthenticated == true,
                ["handlerBound"] = handler != null && slot != null && ReferenceEquals(slot.Current, handler),
                ["handlerRegistered"] = handlers?.Contains(MessagePacker.GetId<ActiveEventChanged>()) == true,
                ["enabled"] = settings.Enabled.Value,
                ["event"] = manager.Current?.Id ?? "",
                ["start"] = manager.Current?.StartedAtMissionTime ?? -1f,
                ["end"] = manager.Current?.EndsAtMissionTime ?? -1f,
                ["cost"] = manager.SupportCostMultiplierFor(0),
                ["history"] = manager.History.Count,
            });
        }

        private static T Find<T>() where T : MonoBehaviour
        {
            foreach (T item in Resources.FindObjectsOfTypeAll<T>())
                if (item != null && item.isActiveAndEnabled) return item;
            return null;
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    }
}
#endif
