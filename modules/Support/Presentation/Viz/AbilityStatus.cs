using System.Globalization;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Viz
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
        public readonly bool Enabled;
        public readonly bool Armed;

        public AbilityFacts(AbilityTone tone, string readiness, string costText, bool enabled, bool armed)
        {
            Tone = tone;
            Readiness = readiness;
            CostText = costText;
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
            return Describe(action, cyber, cost, intel, armed, support.RequestPending || support.CommandPending, support.IsAuthorised(action),
                support.LocalAllocation, support.LocalCooldownRemaining, bypass,
                support.LocalCyber != null ? support.LocalCyber.Intel : 0f, gateOpen, gate);
        }

        /// <summary>The intel price of a CYBER ability or capstone; 0 for everything else.</summary>
        public static float Intel(SupportActionDefinition action) =>
            action.Hack.HasValue ? CyberCatalog.Intel(action.Hack.Value) : action.Cap.HasValue ? Capstones.Intel : 0f;

        /// <summary>
        /// Domain gates the host would refuse (a controlled target sector, a breached Cyber
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
                // The host re-checks the matching post's sector at the clicked point.
                int quality = FieldCatalog.RequiredQuality(action.Field.Value);
                bool eligible = false;
                for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                {
                    FieldTeam team = detachment.Team(i);
                    if (team.State == TeamState.Holding && team.Mission == post && team.Charges > 0 &&
                        team.Quality >= quality && team.PhaseEnd > support.OrbitNow &&
                        OpsSectors.TryLocate(team.X, team.Z, out _)) eligible = true;
                }
                if (!eligible)
                {
                    reason = "NEEDS " + FieldWords.Post(post) + " · QUALITY " + quality + " · 1 CHARGE";
                    return false;
                }
                reason = "IN CONTROLLED " + FieldWords.Post(post) + " SECTOR";
                return true;
            }
            if (action.IsCyber)
            {
                CyberNetwork cyber = support.LocalCyber;
                if (cyber == null || !cyber.HasCommand) return false;
                if (!support.CyberEnabled)
                {
                    reason = "CYBER DISABLED IN HOST CONFIG";
                    return false;
                }
                if (!support.LocalMayCyberControl)
                {
                    reason = "RESERVED · OPERATOR MUST RELEASE TO TEAM";
                    return false;
                }
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
                    ? cyber.Supports(action.Hack.Value, support.OrbitNow)
                    : cyber.AnyCapstone(action.Cap.Value, support.OrbitNow);
                if (!any)
                {
                    reason = action.IsHack
                        ? "NEEDS FRESH ACCESS · QUALITY " + CyberCatalog.RequiredQuality(action.Hack.Value)
                        : "NEEDS QUALITY 6 ACCESS · CHOOSE PAYLOAD";
                    return false;
                }
                CyberNode access = cyber.Node(cyber.AccessSlot);
                if (!cyber.ControlsSector(access.X, access.Z, support.OrbitNow))
                {
                    reason = "NO LIVE SECTOR CONTROL";
                    return false;
                }
                reason = "SECTOR " + OpsSectors.Code(access.X, access.Z) + " · ONE USE · " +
                    CyberWords.Seconds(cyber.AccessRemaining(support.OrbitNow));
                return true;
            }
            TeamGate? gate = action.Id == SupportActionId.FlareMissile ? TeamGate.FlareBarrage :
                action.Id == SupportActionId.Fortify ? TeamGate.Fortify : (TeamGate?)null;
            if (gate.HasValue)
            {
                float left = support.TeamCooldownRemaining(gate.Value);
                if (left > 0.5f)
                {
                    reason = "TEAM RE-TASKING · T-" + Mathf.CeilToInt(left) + "s";
                    return false;
                }
            }
            if (action.Id == SupportActionId.Fortify)
            {
                SpecOpsDetachment detachment = support.LocalDetachment;
                bool safehouse = false;
                if (detachment != null && detachment.Enabled)
                    for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                    {
                        FieldTeam team = detachment.Team(i);
                        if (team.State == TeamState.Holding && team.Mission == FieldMission.Seize && team.Charges > 0 &&
                            team.PhaseEnd > support.OrbitNow && OpsSectors.TryLocate(team.X, team.Z, out _)) safehouse = true;
                    }
                reason = safehouse ? "OWNED GROUND OR HELD SAFEHOUSE SECTOR" : "OWNED GROUND ONLY";
            }
            if (action.Id == SupportActionId.Prsm || action.Id == SupportActionId.Cruise ||
                action.Id == SupportActionId.Artillery || action.Id == SupportActionId.Emp)
            {
                if (support.LocalIntelStale)
                {
                    reason = "STALE INTEL · TASK RADAR SCAN";
                    return false;
                }
            }
            if (action.Id == SupportActionId.Artillery || action.Id == SupportActionId.Emp ||
                action.Id == SupportActionId.Recon || action.Id == SupportActionId.ElintSweep ||
                action.Id == SupportActionId.MtiSweep)
            {
                if (!support.LocalWindowOpen)
                {
                    reason = "OUTSIDE WINDOW · OPENS T-" + PlatformWords.Clock(support.LocalWindowChangeIn);
                    return false;
                }
            }
            PlatformAbility? ability = SupportManager.OrbitalAbility(action.Id);
            if (!ability.HasValue) return true;
            PlatformDenial denial = support.PlatformCheck(ability.Value);
            // An open orbital gate has nothing to add; "READY · READY" helped nobody.
            reason = denial == PlatformDenial.None ? null
                : PlatformWords.Denial(denial, support.LocalPlatform, ability.Value, support.OrbitNow);
            if (denial == PlatformDenial.None && support.LocalPlatform != null && support.LocalPlatform.ObserverPackage &&
                support.LocalPlatform.BoostRemaining(support.OrbitNow) > 0 &&
                support.LocalPlatform.PackageAppliesTo(ability.Value, support.OrbitNow))
                reason = "OBSERVER PACKAGE / INSIDE RECON AREA / " + Mathf.CeilToInt((float)support.LocalPlatform.BoostRemaining(support.OrbitNow)) + " S";
            return denial == PlatformDenial.None;
        }

        public static AbilityFacts Describe(SupportActionDefinition action, bool cyber, float cost, float intel,
            bool armed, bool pending, bool authorised, float allocation, float cooldown, bool bypass,
            float heldIntel, bool gateOpen, string gate)
        {
            string costText = cyber ? Figure(intel) + " INT" : cost > 0f ? Figure(cost) : "—";
            if (!action.Enabled)
                return Make(AbilityTone.Locked, "DISABLED IN HOST CONFIG", costText, false, armed);
            if (cost <= 0f && !cyber)
                return Make(AbilityTone.Locked, "UNAVAILABLE ON THIS MAP", costText, false, armed);
            if (armed)
                return Make(AbilityTone.Armed, "ARMED · RIGHT-CLICK MAP OR TGT MARK", costText, true, true);
            if (pending)
                return Make(AbilityTone.Pending, "REQUEST PENDING · AWAITING HOST", costText, false, false);
            if (!authorised)
                return Make(AbilityTone.Locked, (action.IsCyber || action.IsField) && !string.IsNullOrEmpty(gate)
                    ? gate : Locked(action), costText, false, false);
            if (!gateOpen)
                return Make(AbilityTone.Locked, gate, costText, false, false);
            if (cooldown > 0.5f)
                return Make(AbilityTone.Armed, "NET COOLING · T-" + Mathf.CeilToInt(cooldown) + "s", costText, false, false);
            if (!cyber && !bypass && allocation + 0.001f < cost)
                return Make(AbilityTone.Danger, "INSUFFICIENT ALLOCATION", costText, false, false);
            if (cyber && !bypass && heldIntel + 0.001f < intel)
                return Make(AbilityTone.Danger, "INSUFFICIENT INTEL", costText, false, false);
            string ready = gate != null ? "READY · " + gate : "READY · ARM, THEN RIGHT-CLICK MAP";
            return Make(AbilityTone.Ready, ready, costText, true, false);
        }

        private static string Locked(SupportActionDefinition action)
        {
            if (action.IsField) return "LOCKED · " + FieldWords.AbilityLocked(action.Field.Value);
            if (action.IsHack) return "LOCKED · PREPARE ACCESS";
            if (action.IsCapstone) return "LOCKED · QUALITY 6 ACCESS + CHOSEN PAYLOAD";
            if (action.Id == SupportActionId.FlareMissile) return "LOCKED · SHARES RADAR SCAN PERK";
            if (action.Id == SupportActionId.Fortify) return "LOCKED · UNLOCK FORTIFY IN SQD ABILITIES";
            return "LOCKED · UNLOCK IN SQD ABILITIES";
        }

        private static AbilityFacts Make(AbilityTone tone, string readiness, string cost, bool enabled, bool armed) =>
            new AbilityFacts(tone, readiness, cost, enabled, armed);

        private static string Figure(float value) => Mathf.Round(value).ToString("N0", Invariant);
    }
}
