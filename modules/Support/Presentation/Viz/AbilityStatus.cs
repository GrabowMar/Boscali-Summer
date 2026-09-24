using System.Globalization;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    internal enum AbilityTone : byte
    {
        Locked,
        Ready,
        Armed,
        Pending,
        Danger
    }

    /// <summary>The facts every ability surface shows. It draws nothing.</summary>
    internal readonly struct AbilityFacts
    {
        public readonly AbilityTone Tone;
        public readonly string Readiness;
        public readonly string CostText;
        public readonly string Recharge;
        public readonly string Coverage;
        public readonly bool Enabled;
        public readonly bool Armed;

        public AbilityFacts(AbilityTone tone, string readiness, string costText, string recharge, string coverage,
            bool enabled, bool armed)
        {
            Tone = tone;
            Readiness = readiness;
            CostText = costText;
            Recharge = recharge;
            Coverage = coverage;
            Enabled = enabled;
            Armed = armed;
        }
    }

    /// <summary>
    /// One decision for an ability row. The OPS pages and the rooms all read this, so a cost or a
    /// refusal cannot say two different things.
    /// </summary>
    internal static class AbilityStatus
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>
        /// Every fact for one action, read from the manager the way the host would judge it. The MFD
        /// rows, the three ACTIONS pages and the room softkeys all call this, never their own copy.
        /// </summary>
        public static AbilityFacts For(SupportManager support, SupportActionDefinition action, bool bypass)
        {
            bool cyber = action.IsCyber;
            float cost = cyber ? 0f : support.Cost(action);
            float intel = Intel(action);
            bool armed = support.ArmedAction.HasValue && support.ArmedAction.Value == action.Id;
            bool gateOpen = Gate(support, action, out string gate);
            return Describe(action, cyber, cost, intel, armed, support.RequestPending, support.IsAuthorised(action),
                support.LocalAllocation, support.LocalCooldownRemaining, bypass,
                support.LocalCyber != null ? support.LocalCyber.Intel : 0f, gateOpen, gate);
        }

        /// <summary>The intel price of a CYBER ability or capstone; 0 for everything else.</summary>
        public static float Intel(SupportActionDefinition action) =>
            action.Hack.HasValue ? CyberCatalog.Intel(action.Hack.Value) : action.Cap.HasValue ? Capstones.Intel : 0f;

        /// <summary>
        /// Domain gates the host would refuse (a location covering the target, a breached Cyber
        /// Command, the station's pass and power, a held post). An open gate still reports the
        /// coverage note the player needs.
        /// </summary>
        public static bool Gate(SupportManager support, SupportActionDefinition action, out string reason)
        {
            reason = null;
            if (action.IsField)
            {
                SpecOpsDetachment detachment = support.LocalDetachment;
                if (detachment == null || !detachment.Enabled) return false;
                float recharge = detachment.AbilityRechargeRemaining(action.Field.Value, support.OrbitNow);
                if (recharge > 0f)
                {
                    reason = "RECHARGING · T-" + Mathf.CeilToInt(recharge) + "s";
                    return false;
                }
                FieldMission post = FieldCatalog.PostFor(action.Field.Value);
                // The host re-checks that a post's reach covers the clicked point.
                // Zero posts is only reachable under the debug bypass; say so instead of "0 POSTS".
                int posts = detachment.Posts(post);
                reason = posts == 0
                    ? "NO " + FieldWords.Post(post) + " · DEBUG BYPASS"
                    : "WITHIN " + FieldWords.Km(FieldCatalog.PostReach(post)).ToUpperInvariant() + " OF " +
                      (posts == 1 ? "YOUR " + FieldWords.Post(post) : posts + " " + FieldWords.Post(post) + "S");
                return true;
            }
            if (action.IsCyber)
            {
                CyberNetwork cyber = support.LocalCyber;
                if (cyber == null || !cyber.HasCommand) return false;
                if (cyber.CommandCompromised)
                {
                    reason = "C2 BREACHED · PATCH IT";
                    return false;
                }
                if (action.Cap.HasValue && cyber.CapstoneRechargeRemaining(action.Cap.Value, support.OrbitNow) > 0f)
                {
                    reason = "RECHARGING · " + CyberWords.Seconds(
                        cyber.CapstoneRechargeRemaining(action.Cap.Value, support.OrbitNow));
                    return false;
                }
                bool any = action.Hack.HasValue
                    ? cyber.AnyTier(CyberCatalog.RequiredStage(action.Hack.Value) - 1)
                    : cyber.AnyCapstone(action.Cap.Value);
                if (!any)
                {
                    reason = action.IsHack
                        ? "NEEDS A STAGE-" + CyberCatalog.RequiredStage(action.Hack.Value) + " LOCATION"
                        : "NEEDS A MASTERED LOCATION";
                    return false;
                }
                // The host still re-checks that a location's radius covers the point.
                reason = "TARGET MUST BE INSIDE A LOCATION'S RADIUS";
                return true;
            }
            PlatformAbility? ability = SupportManager.OrbitalAbility(action.Id);
            if (!ability.HasValue) return true;
            PlatformDenial denial = support.PlatformCheck(ability.Value);
            // An open orbital gate has nothing to add; "READY · READY" helped nobody.
            reason = denial == PlatformDenial.None ? null
                : PlatformWords.Denial(denial, support.LocalPlatform, ability.Value, support.OrbitNow, support.OrbitClock);
            return denial == PlatformDenial.None;
        }

        public static AbilityFacts Describe(SupportActionDefinition action, bool cyber, float cost, float intel,
            bool armed, bool pending, bool authorised, float allocation, float cooldown, bool bypass,
            float heldIntel, bool gateOpen, string gate)
        {
            string costText = cyber ? Figure(intel) + " INT" : cost > 0f ? Figure(cost) : "—";
            string recharge = "";
            string coverage = gate ?? "";
            if (!action.Enabled)
                return Make(AbilityTone.Locked, "DISABLED IN HOST CONFIG", costText, recharge, coverage, false, armed);
            if (cost <= 0f && !cyber)
                return Make(AbilityTone.Locked, "UNAVAILABLE ON THIS MAP", costText, recharge, coverage, false, armed);
            if (armed)
                return Make(AbilityTone.Armed, "ARMED · RIGHT-CLICK MAP OR TGT MARK", costText, recharge, coverage, true, true);
            if (pending)
                return Make(AbilityTone.Pending, "REQUEST PENDING · AWAITING HOST", costText, recharge, coverage, false, false);
            if (!authorised)
                return Make(AbilityTone.Locked, Locked(action), costText, recharge, coverage, false, false);
            if (!gateOpen)
                return Make(AbilityTone.Locked, gate, costText, recharge, coverage, false, false);
            if (cooldown > 0.5f)
            {
                recharge = "NET COOLING · T-" + Mathf.CeilToInt(cooldown) + "s";
                return Make(AbilityTone.Armed, recharge, costText, recharge, coverage, false, false);
            }
            if (!cyber && !bypass && allocation + 0.001f < cost)
                return Make(AbilityTone.Danger, "INSUFFICIENT ALLOCATION", costText, recharge, coverage, false, false);
            if (cyber && !bypass && heldIntel + 0.001f < intel)
                return Make(AbilityTone.Danger, "INSUFFICIENT INTEL", costText, recharge, coverage, false, false);
            string ready = gate != null ? "READY · " + gate : "READY · ARM, THEN RIGHT-CLICK MAP";
            return Make(AbilityTone.Ready, ready, costText, recharge, coverage, true, false);
        }

        private static string Locked(SupportActionDefinition action)
        {
            if (action.IsField) return "LOCKED · " + FieldWords.AbilityLocked(action.Field.Value);
            if (action.IsHack) return "LOCKED · BREACH A LOCATION TO STAGE " + CyberCatalog.RequiredStage(action.Hack.Value);
            if (action.IsCapstone) return "LOCKED · MASTER A LOCATION (STAGE 4)";
            if (action.Id == SupportActionId.FlareMissile) return "LOCKED · SHARES RADAR SCAN PERK";
            return "LOCKED · UNLOCK IN SQD ABILITIES";
        }

        private static AbilityFacts Make(AbilityTone tone, string readiness, string cost, string recharge,
            string coverage, bool enabled, bool armed) =>
            new AbilityFacts(tone, readiness, cost, recharge, coverage, enabled, armed);

        private static string Figure(float value) => Mathf.Round(value).ToString("N0", Invariant);
    }
}
