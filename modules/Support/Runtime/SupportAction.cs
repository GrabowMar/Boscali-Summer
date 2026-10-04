using System.Collections;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Support.Configuration;
using NuclearOption.Networking;
using UnityEngine;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Bounded concurrency pools the host hands out to actions.</summary>
    internal enum SupportPool : byte
    {
        Strike = 0,
        Cyber = 1
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
        int SceneGeneration { get; }
        bool TryGetSpaceState(FactionHQ owner, out SpaceState state);
        int OpenSpaceWindow(FactionHQ owner, GlobalPosition point, float radius, BirdKind source,
            float minimumSpeed, float maximumSpeed);

        bool TryReserve(FactionHQ owner, SupportPool pool);
        void Release(FactionHQ owner, SupportPool pool);
        void Run(IEnumerator routine);

        /// <summary>Number of contacts a sweep produced, echoed to the requester's reply.</summary>
        void ReportContacts(int requestId, int contacts);

        /// <summary>Live time-of-flight of the strike's first missile, echoed to the reply.</summary>
        void ReportTti(int requestId, float seconds);

        /// <summary>Records a cruise salvo for Tier-2 tasking; the first missile seeds the record.</summary>
        void TrackCruiseStrike(int requestId, ulong requesterId, FactionHQ owner, GlobalPosition target, Missile first);

        /// <summary>Appends a later salvo missile to its strike record.</summary>
        void AddCruiseMissile(int requestId, Missile missile);
    }

    internal readonly struct SupportContext
    {
        public readonly Player Player;
        public readonly FactionHQ Owner;
        public readonly GlobalPosition Target;
        public readonly int RequestId;
        public readonly ISupportHost Host;
        public readonly SpaceActionTransaction SpaceTask;
        /// <summary>Set only for a claimed TASKED call: the action must report its physical launch to this job.</summary>
        public readonly TaskedLaunchJob Tasked;

        public SupportContext(Player player, GlobalPosition target, int requestId, ISupportHost host,
            SpaceActionTransaction spaceTask = null, TaskedLaunchJob tasked = null)
        {
            Player = player;
            Owner = player == null ? null : player.HQ;
            Target = target;
            RequestId = requestId;
            Host = host;
            SpaceTask = spaceTask;
            Tasked = tasked;
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
        /// Availability flag: 1 when this action is usable on this map, 0 when it is not (the manager reports
        /// CapabilityUnavailable). The price is CallSheet x CallPricing, never this value.
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
            Prefix + kind + ":" + Core.Contracts.PlayerIdentity.Of(context.Player) + ":" +
            context.RequestId;
    }

    internal sealed class SupportActionDefinition
    {
        public readonly SupportActionId Id;
        public readonly string Name;
        public readonly string Description;
        public readonly string Capability;
        public readonly ISupportAction Action;
        public readonly SpaceBirdRequirement RequiredBird;
        public readonly BirdTask? SpaceTask;
        public readonly float TaskSeconds;
        public readonly bool RequiresPhysicalLaunch;

        private readonly ConfigEntry<bool> enabled;

        public SupportActionDefinition(
            SupportActionId id, string name, string description, string capability,
            ConfigEntry<bool> enabled, ISupportAction action,
            SpaceBirdRequirement requiredBird = SpaceBirdRequirement.None, BirdTask? spaceTask = null,
            float taskSeconds = 0f, bool requiresPhysicalLaunch = false)
        {
            Id = id;
            Name = name;
            Description = description;
            Capability = capability;
            this.enabled = enabled;
            Action = action;
            RequiredBird = requiredBird;
            SpaceTask = spaceTask;
            TaskSeconds = taskSeconds;
            RequiresPhysicalLaunch = requiresPhysicalLaunch;
        }

        public bool Enabled => enabled == null || enabled.Value;
    }
}
