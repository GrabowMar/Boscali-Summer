using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Runtime.Actions;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Support.Runtime
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
            actions.Add(new SupportActionDefinition(
                SupportActionId.Recon, "RADAR SCAN",
                "Core survey radar reveals stationary ground contacts. An imager doubles the scene; fresh recon assists fire-control tracking.",
                SupportCapabilities.Recon, settings.ReconEnabled, new ReconAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.ElintSweep, "ELINT SWEEP",
                "Station SIGINT array locates enemy ground radars that are emitting.",
                SupportCapabilities.Recon, settings.ElintEnabled, new ElintAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.MtiSweep, "MTI SWEEP",
                "Core radar tracks moving ground contacts; an imager doubles the scene. Stationary targets blend into the ground return.",
                SupportCapabilities.Recon, settings.MtiEnabled, new MtiAction()));

            if (fortifications != null)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Fortify, "ZONE FORTIFICATION",
                    "Reinforce a controlled zone with defenders.",
                    SupportCapabilities.Fortify, settings.FortifyEnabled, new FortifyAction(fortifications)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Artillery, "ROD FROM GOD",
                "One rod per online magazine, bounded by ammunition. Gyros halve scatter; a banked STRIKE package tightens it up to 40% further.",
                SupportCapabilities.Artillery, settings.ArtilleryEnabled, new ArtilleryAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Prsm, "PRSM STRIKE",
                "One offboard ballistic missile onto the mark. Needs fresh HQ intel at the target.",
                SupportCapabilities.Artillery, settings.PrsmEnabled, new PrsmAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Cruise, "CRUISE SALVO",
                "Bounded salvo of offboard cruise missiles onto the mark, counted against the faction live cap.",
                SupportCapabilities.Artillery, settings.CruiseEnabled, new CruiseAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Emp, "EMP SHOCK",
                "30 s hostile radar blackout. Batteries widen the pulse; a banked SCREEN package adds up to 25% radius and duration.",
                SupportCapabilities.Emp, settings.EmpEnabled, new EmpAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.FlareMissile, "FLARE BARRAGE",
                "Airburst IR countermeasure cloud.",
                SupportCapabilities.Recon, settings.FlareBarrageEnabled, new FlareMissileAction()));

            // Always built: the rows' live CyberOperations gate refuses them while the host has it off,
            // so a peer that booted with it off still matches a host that has it on.
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

            actions.Add(new SupportActionDefinition(
                SupportActionId.JtacMark, "JTAC MARK",
                "SOF team lases the nearest hostile unit at the mark; friendly laser-guided weapons see it.",
                SupportCapabilities.Recon, settings.SpecOpsEnabled, new MarkAction()));
            actions.Add(new SupportActionDefinition(
                SupportActionId.JtacUnlase, "JTAC UNLASE",
                "Clears one lase reference on the nearest hostile unit at the mark.",
                SupportCapabilities.Recon, settings.SpecOpsEnabled, new UnlaseAction()));
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
