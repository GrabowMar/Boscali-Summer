using System.Collections;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>Bounded concurrency pools the host hands out to actions.</summary>
    internal enum SupportPool : byte
    {
        Strike = 0
    }

    /// <summary>
    /// What an action is allowed to ask of the feature: bounded slots, coroutines,
    /// settings and logging. Keeping this narrow is what stops an action from growing
    /// its own lifecycle.
    /// </summary>
    internal interface ISupportHost
    {
        SupportSettings Settings { get; }
        ManualLogSource Logger { get; }
        VanillaSupportCatalog Vanilla { get; }

        bool TryReserve(SupportPool pool);
        void Release(SupportPool pool);
        void Run(IEnumerator routine);
    }

    internal readonly struct SupportContext
    {
        public readonly Player Player;
        public readonly FactionHQ Owner;
        public readonly GlobalPosition Target;
        public readonly int RequestId;
        public readonly ISupportHost Host;

        public SupportContext(Player player, GlobalPosition target, int requestId, ISupportHost host)
        {
            Player = player;
            Owner = player == null ? null : player.HQ;
            Target = target;
            RequestId = requestId;
            Host = host;
        }

        public SupportSettings Settings => Host.Settings;
        public ManualLogSource Logger => Host.Logger;
    }

    /// <summary>
    /// One support action. Adding an action is this interface plus one catalogue row — no
    /// change to the manager, the network layer or the panel.
    /// </summary>
    internal interface ISupportAction
    {
        /// <summary>
        /// Allocation price before perk discounts, derived from vanilla unit value where the
        /// action spawns something. Returns 0 when the action cannot currently price itself,
        /// which the manager treats as unavailable rather than free.
        /// </summary>
        float BaseCost(in SupportContext context);

        SupportResult Execute(in SupportContext context);
    }

    /// <summary>
    /// Unique names for spawned support objects. Stable and per-request so a replayed request
    /// cannot produce two units with the same identity.
    /// </summary>
    internal static class SupportNaming
    {
        public const string Prefix = "BoscaliSummer:Support:";

        public static string Unique(string kind, in SupportContext context) =>
            Prefix + kind + ":" + Framework.Contracts.PlayerIdentity.Of(context.Player) + ":" +
            context.RequestId;

        public static string Unique(string kind, in SupportContext context, int index) =>
            Unique(kind, context) + ":" + index;
    }

    internal sealed class SupportActionDefinition
    {
        public readonly SupportActionId Id;
        public readonly string Name;
        public readonly string Description;
        public readonly string Capability;
        public readonly ISupportAction Action;

        private readonly ConfigEntry<bool> enabled;

        public SupportActionDefinition(
            SupportActionId id, string name, string description, string capability,
            ConfigEntry<bool> enabled, ISupportAction action)
        {
            Id = id;
            Name = name;
            Description = description;
            Capability = capability;
            this.enabled = enabled;
            Action = action;
        }

        public bool Enabled => enabled == null || enabled.Value;
    }
}
