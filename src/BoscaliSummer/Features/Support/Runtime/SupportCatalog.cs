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
        private readonly List<SupportActionDefinition> actions = new List<SupportActionDefinition>(5);

        public SupportCatalog(
            SupportSettings settings, IZoneFortificationService fortifications,
            IFireSuppressionService fireSuppression = null)
        {
            if (VanillaSupportCatalog.ReconAvailable)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Recon, "SATELLITE SCAN",
                    "Reveal hostile units across a wide area via satellite.",
                    SupportCapabilities.Recon, settings.ReconEnabled, new ReconAction()));

            if (fortifications != null)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Fortify, "ZONE FORTIFICATION",
                    "Reinforce a controlled airbase or captured strategic zone.",
                    SupportCapabilities.Fortify, settings.FortifyEnabled, new FortifyAction(fortifications)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Artillery, "ROD FROM GOD",
                "Orbital kinetic strike: one high-velocity projectile onto the mark.",
                SupportCapabilities.Artillery, settings.ArtilleryEnabled, new ArtilleryAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Emp, "EMP SHOCK",
                "A high-altitude burst that blinds radars across a wide area - friend and foe alike.",
                SupportCapabilities.Emp, settings.EmpEnabled, new EmpAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.SmokeMarker, "SMOKE DESIGNATION",
                "CAS marker: deploy a high-visibility signaling smoke plume on the target grid.",
                SupportCapabilities.Recon, settings.ReconEnabled, new SmokeMarkerAction(fireSuppression)));
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
