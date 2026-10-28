using System.Collections;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Support.Configuration;
using NuclearOption.Networking;
using BoscaliSummer.Modules.Support.Domain.Fronts;
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
        int OpenSpaceWindow(FactionHQ owner, GlobalPosition point, float radius, BirdKind source,
            float minimumSpeed, float maximumSpeed);

        /// <summary>
        /// Opens the OPTICAL reveal window at the point, sized by the sky there. Returns the contacts admitted, or -1 with the
        /// refusal (OpticalNight, SkyUnknown or SpawnFailed) when no window opened.
        /// </summary>
        int OpenOpticalWindow(FactionHQ owner, GlobalPosition point, float baseRadius, out SupportResult refusal);

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
        void AddCruiseMissile(ulong requesterId, int requestId, Missile missile);

        /// <summary>
        /// Starts a CYBER package effect (RADAR BLIND = JAM RADAR, SAM NET DOWN) at the point for the faction, against every other faction.
        /// <paramref name="power"/> scales its radius and duration and deepens a jam; <paramref name="durationBonus"/> (1 = none) is the room bonus on the duration alone.
        /// False when the faction has no CYBER desk or its effect book is full.
        /// </summary>
        bool StartCyberPerk(FactionHQ owner, SupportActionId package, GlobalPosition target, float power, float durationBonus = 1f);

        /// <summary>RECON TEAM: a reveal of enemy ground units around the point. NoCamp unless the faction has a live camp.</summary>
        SupportResult StartSofRecon(FactionHQ owner, GlobalPosition target, float power);

        /// <summary>
        /// SABOTAGE STRIKE: the nearest enemy anchor within 1 km of the point is attacked; the team's odds (<paramref name="power"/>, live camps) decide whether it goes down. NoCamp without a live camp,
        /// NoAnchor with no target. <paramref name="succeeded"/> says how the roll went.
        /// </summary>
        SupportResult StartSofSabotage(FactionHQ owner, GlobalPosition target, float power, out bool succeeded);

        /// <summary>The faction's own counts on every front as the host's 5 s world read left them (empty when unknown or on a client).</summary>
        FrontSides Facts(FactionHQ owner);

        /// <summary>How many of the faction's own satellites are up and cover the point (0 .. 3).</summary>
        int BirdsCovering(FactionHQ owner, GlobalPosition at);

        /// <summary>OVERWATCH: follows the moving enemy ground units nearest the point on the faction's map. NoMovers when none moves within the radius. <paramref name="followed"/> is how many are kept.</summary>
        SupportResult StartOverwatch(FactionHQ owner, GlobalPosition at, float radius, int units, float seconds, out int followed);

        /// <summary>SIPHON: moves funds from the strongest enemy's front budgets into the faction's CYBER budget. NothingToSiphon below the minimum. <paramref name="taken"/> is the amount moved.</summary>
        SupportResult StartSiphon(FactionHQ owner, float power, out int taken);

        /// <summary>DIRECT ACTION: a ready team (the pilot's own, or OVERLORD's) raids the revealed SAM, radar or anchor nearest the point. NoReadyTeam / NoHighValue when it cannot.</summary>
        SupportResult StartDirectAction(Player pilot, GlobalPosition at, float power);
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
        /// <summary>The perk's front POWER (<see cref="IFrontReadiness.Power"/>, 0.6 .. 1.5): scales radius, duration, strikes, damage and odds.</summary>
        public readonly float Power;
        /// <summary>The aim was snapped to a fresh own-faction MARK: the strike uses the tight optical/SAR disk, not the standard CEP.</summary>
        public readonly bool MarkSnapped, MarkSar;

        public SupportContext(Player player, GlobalPosition target, int requestId, ISupportHost host,
            SpaceActionTransaction spaceTask = null, TaskedLaunchJob tasked = null, float power = 1f, bool markSnapped = false, bool markSar = false)
        {
            MarkSnapped = markSnapped; MarkSar = markSar;
            Power = FrontRules.Sane(power);
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
