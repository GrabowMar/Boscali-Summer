using System.Collections.Generic;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Runtime.Actions;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// The one place an action is declared. Adding a support action is a row here plus one
    /// <see cref="ISupportAction"/> file and one perk row that grants its capability. Cyber
    /// operations are rows too, gated by a <c>CyberNetwork</c> stage or capstone instead
    /// of a career perk, and so are SPEC OPS SPOT and SUPPRESS, gated by a held post. The manager, the network layer and the panel are all driven from
    /// this table. An action whose game capability cannot be resolved is left out entirely
    /// rather than rendered and then failing at request time.
    /// </summary>
    internal sealed class SupportCatalog
    {
        private readonly List<SupportActionDefinition> actions = new List<SupportActionDefinition>(10);

        public SupportCatalog(
            SupportSettings settings, IZoneFortificationService fortifications)
        {
            if (VanillaSupportCatalog.ReconAvailable)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Recon, "RADAR SCAN",
                    "Station radar images a scene; stationary ground contacts are revealed.",
                    SupportCapabilities.Recon, settings.ReconEnabled, new ReconAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.ElintSweep, "ELINT SWEEP",
                "Station SIGINT array locates enemy ground radars that are emitting.",
                SupportCapabilities.Recon, settings.ElintEnabled, new ElintAction()));

            if (VanillaSupportCatalog.ReconAvailable)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.MtiSweep, "MTI SWEEP",
                    "Station radar tracks moving ground contacts in the imaged scene; stationary targets blend into the ground return.",
                    SupportCapabilities.Recon, settings.MtiEnabled, new MtiAction()));

            if (fortifications != null)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Fortify, "ZONE FORTIFICATION",
                    "Reinforce a controlled zone with defenders.",
                    SupportCapabilities.Fortify, settings.FortifyEnabled, new FortifyAction(fortifications)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Artillery, "ROD FROM GOD",
                "One kinetic rod per online magazine, up to loaded ammunition.",
                SupportCapabilities.Artillery, settings.ArtilleryEnabled, new ArtilleryAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Emp, "EMP SHOCK",
                "High-altitude airburst and 30 s radar blackout. Extra station batteries widen the pulse; hits friend and foe.",
                SupportCapabilities.Emp, settings.EmpEnabled, new EmpAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.FlareMissile, "FLARE BARRAGE",
                "Airburst IR countermeasure cloud.",
                SupportCapabilities.Recon, settings.FlareBarrageEnabled, new FlareMissileAction()));

            if (settings.CyberEnabled.Value)
            {
                AddHack(settings, SupportActionId.HackPing, HackKind.Ping);
                AddHack(settings, SupportActionId.HackTrack, HackKind.Track);
                AddHack(settings, SupportActionId.HackBlackout, HackKind.Blackout);
                AddHack(settings, SupportActionId.HackGhost, HackKind.Ghost);
                AddHack(settings, SupportActionId.HackSpoof, HackKind.Spoof);
                AddHack(settings, SupportActionId.HackScan, HackKind.Scan);
                AddHack(settings, SupportActionId.HackHijack, HackKind.Hijack);
                AddHack(settings, SupportActionId.HackOverload, HackKind.Overload);
                AddCapstone(settings, SupportActionId.CapReveal, Capstone.Reveal);
                AddCapstone(settings, SupportActionId.CapJammer, Capstone.Jammer);
                AddCapstone(settings, SupportActionId.CapSabotage, Capstone.Sabotage);
            }

            // SPEC OPS: gated by a held post, like the CYBER abilities, never by a perk.
            actions.Add(new SupportActionDefinition(SupportActionId.SpecSpot, FieldAbility.Spot,
                settings.SpecOpsEnabled, new FieldAbilityAction(FieldAbility.Spot)));
            actions.Add(new SupportActionDefinition(SupportActionId.SpecSuppress, FieldAbility.Suppress,
                settings.SpecOpsEnabled, new FieldAbilityAction(FieldAbility.Suppress)));
            actions.Add(new SupportActionDefinition(SupportActionId.SpecSkywatch, FieldAbility.Skywatch,
                settings.SpecOpsEnabled, new FieldAbilityAction(FieldAbility.Skywatch)));
            actions.Add(new SupportActionDefinition(SupportActionId.SpecEavesdrop, FieldAbility.Eavesdrop,
                settings.SpecOpsEnabled, new FieldAbilityAction(FieldAbility.Eavesdrop)));
            actions.Add(new SupportActionDefinition(SupportActionId.SpecHunt, FieldAbility.Hunt,
                settings.SpecOpsEnabled, new FieldAbilityAction(FieldAbility.Hunt)));
        }

        public IReadOnlyList<SupportActionDefinition> Actions => actions;

        public SupportActionDefinition Find(SupportActionId id)
        {
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].Id == id) return actions[i];
            return null;
        }

        private void AddHack(SupportSettings settings, SupportActionId id, HackKind kind) =>
            actions.Add(new SupportActionDefinition(id, kind, settings.CyberEnabled, new HackAction(kind)));

        private void AddCapstone(SupportSettings settings, SupportActionId id, Capstone capstone) =>
            actions.Add(new SupportActionDefinition(id, capstone, settings.CyberEnabled, new CapstoneAction(capstone)));
    }
}
