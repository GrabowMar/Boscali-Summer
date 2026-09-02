using System.Collections.Generic;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Runtime.Actions;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// The one place an action is declared. Adding an action is a row here plus one
    /// <see cref="ISupportAction"/> file and one perk row that grants its capability —
    /// the manager, the network layer and the panel are all driven from this table.
    /// An action whose game capability cannot be resolved is left out entirely rather than
    /// rendered and then failing at request time.
    /// </summary>
    internal sealed class SupportCatalog
    {
        private readonly List<SupportActionDefinition> actions = new List<SupportActionDefinition>(6);

        public SupportCatalog(SupportSettings settings, IZoneFortificationService fortifications)
        {
            actions.Add(new SupportActionDefinition(
                SupportActionId.Airdrop, "VEHICLE AIRDROP",
                "Air-drop an armoured vehicle at marked coordinates.",
                SupportCapabilities.Airdrop, settings.AirdropEnabled, new AirdropAction(false)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.AirDefenceDrop, "AIR DEFENCE AIRDROP",
                "Air-drop a mobile air-defence vehicle at marked coordinates.",
                SupportCapabilities.AirDefenceDrop, settings.AirDefenceDropEnabled, new AirdropAction(true)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Convoy, "GROUND CONVOY",
                "Requisition a ground convoy at the friendly airbase nearest the mark.",
                SupportCapabilities.Convoy, settings.ConvoyEnabled, new ConvoyAction()));

            if (VanillaSupportCatalog.ReconAvailable)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Recon, "RECON SWEEP",
                    "Reveal hostile units around the marked coordinates.",
                    SupportCapabilities.Recon, settings.ReconEnabled, new ReconAction()));

            if (fortifications != null)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Fortify, "ZONE FORTIFICATION",
                    "Reinforce a controlled airbase or captured strategic zone.",
                    SupportCapabilities.Fortify, settings.FortifyEnabled, new FortifyAction(fortifications)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Artillery, "ARTILLERY FIRE MISSION",
                "Precision artillery salvo on the selected target coordinates.",
                SupportCapabilities.Artillery, settings.ArtilleryEnabled, new ArtilleryAction()));
        }

        public IReadOnlyList<SupportActionDefinition> Actions => actions;

        public SupportActionDefinition Find(SupportActionId id)
        {
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].Id == id) return actions[i];
            return null;
        }
    }
}
