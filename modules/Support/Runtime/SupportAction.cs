using System.Collections;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
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
        SpaceOperations Space { get; }

        /// <summary>The clock station passes are computed against on this peer.</summary>
        double OrbitNow { get; }

        OrbitClock OrbitClock { get; }
        ManualLogSource Logger { get; }
        VanillaSupportCatalog Vanilla { get; }

        bool TryReserve(SupportPool pool);
        void Release(SupportPool pool);
        void Run(IEnumerator routine);

        /// <summary>Number of contacts a sweep produced, echoed to the requester's reply.</summary>
        void ReportContacts(int requestId, int contacts);

        /// <summary>Host-side entry for the two track-deception operations.</summary>
        bool BeginDeception(Player caster, HackKind kind, GlobalPosition target, float duration);

        /// <summary>The faction's electronic-warfare presence, or null if none is deployed.</summary>
        EwAsset EwAssetFor(FactionHQ owner);
    }

    internal readonly struct SupportContext
    {
        public readonly Player Player;
        public readonly FactionHQ Owner;
        public readonly GlobalPosition Target;
        public readonly int RequestId;
        public readonly ISupportHost Host;

        /// <summary>
        /// The requester's `SupportEffectScale` multiplier, resolved once by the host so an
        /// action never has to find its own way back to the perk state. 1 when nobody scales.
        /// </summary>
        public readonly float EffectScale;

        public SupportContext(Player player, GlobalPosition target, int requestId, ISupportHost host,
            float effectScale = 1f)
        {
            Player = player;
            Owner = player == null ? null : player.HQ;
            Target = target;
            RequestId = requestId;
            Host = host;
            EffectScale = effectScale;
        }

        public SupportSettings Settings => Host.Settings;
        public ManualLogSource Logger => Host.Logger;

        /// <summary>Requester's faction infrastructure (null until the theater is loaded).</summary>
        public InfoNetwork Info => Host.Space.InfoFor(Owner);

        /// <summary>
        /// The requester's station when it can run <paramref name="ability"/> right now (fitted,
        /// online, powered, overhead, charged, recharged, armed); null with the reason otherwise.
        /// Nothing is spent here — call <see cref="OrbitalPlatform.Consume"/> once the action is accepted.
        /// </summary>
        public OrbitalPlatform PlatformAccess(PlatformAbility ability, out PlatformDenial denial)
        {
            OrbitalPlatform platform = Host.Space.PlatformFor(Owner);
            if (platform == null)
            {
                denial = PlatformDenial.NoPlatform;
                return null;
            }
            denial = platform.Check(ability, Host.OrbitNow, Host.OrbitClock);
            return denial == PlatformDenial.None ? platform : null;
        }

        public static SupportResult Refusal(PlatformDenial denial)
        {
            switch (denial)
            {
                case PlatformDenial.NoPlatform: return SupportResult.NoPlatform;
                case PlatformDenial.NotFitted: return SupportResult.ModuleNotFitted;
                case PlatformDenial.Offline: return SupportResult.ModuleOffline;
                case PlatformDenial.Brownout: return SupportResult.PlatformBrownout;
                case PlatformDenial.LowEnergy: return SupportResult.PlatformLowPower;
                case PlatformDenial.Recharging: return SupportResult.PlatformRecharging;
                case PlatformDenial.Expended: return SupportResult.PlatformExpended;
                case PlatformDenial.NoFuel: return SupportResult.NoFuel;
                default: return SupportResult.OutOfCoverage;
            }
        }

        /// <summary>The requester's EW asset, or null if none is deployed / it has died.</summary>
        public EwAsset EwAsset => Host.EwAssetFor(Owner);
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

        /// <summary>Facility gate for cyber operations; null for perk-authorised actions.</summary>
        public readonly HackKind? Hack;

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
            Hack = null;
        }

        public SupportActionDefinition(
            SupportActionId id, HackKind hack,
            ConfigEntry<bool> enabled, ISupportAction action)
        {
            Id = id;
            Name = CyberCatalog.Name(hack);
            Description = CyberCatalog.Description(hack);
            Capability = null;
            this.enabled = enabled;
            Action = action;
            Hack = hack;
        }

        public bool IsHack => Hack.HasValue;
        public bool Enabled => enabled == null || enabled.Value;
    }
}
