using System.Collections.Generic;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Runtime.Actions;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// The one place an action is declared. Adding a support action is a row here plus one
    /// <see cref="ISupportAction"/> file and one perk row that grants its capability. Cyber
    /// operations are rows too, gated by an <see cref="InfoNetwork"/> facility level instead
    /// of a career perk. The manager, the network layer and the panel are all driven from
    /// this table. An action whose game capability cannot be resolved is left out entirely
    /// rather than rendered and then failing at request time.
    /// </summary>
    internal sealed class SupportCatalog
    {
        private readonly List<SupportActionDefinition> actions = new List<SupportActionDefinition>(10);

        public SupportCatalog(
            SupportSettings settings, IZoneFortificationService fortifications,
            IFireSuppressionService fireSuppression = null)
        {
            if (VanillaSupportCatalog.ReconAvailable)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Recon, "SATELLITE SCAN",
                    "Reveal hostiles under a recon satellite.",
                    SupportCapabilities.Recon, settings.ReconEnabled, new ReconAction()));

            if (fortifications != null)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Fortify, "ZONE FORTIFICATION",
                    "Reinforce a controlled zone with defenders.",
                    SupportCapabilities.Fortify, settings.FortifyEnabled, new FortifyAction(fortifications)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Artillery, "ROD FROM GOD",
                "Kinetic strike from a strike satellite overhead.",
                SupportCapabilities.Artillery, settings.ArtilleryEnabled, new ArtilleryAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Emp, "EMP SHOCK",
                "Radar disruption from an EW satellite. Hits friend and foe.",
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
            }
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
    }
}
