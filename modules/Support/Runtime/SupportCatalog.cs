using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Configuration;
using BoscaliSummer.Modules.Support.Runtime.Actions;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The one place an action is declared. Adding a support action is a row here plus one
    /// <see cref="ISupportAction"/> file and one perk row that grants its capability. The manager, the network layer and the CALLS page are all driven from
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
                "RADAR bird reveals stationary ground contacts through a live uplink.",
                SupportCapabilities.Recon, settings.ReconEnabled, new ReconAction(),
                SpaceBirdRequirement.Radar, BirdTask.Scan, SpaceRevealWindow.BirdBusySeconds));

            actions.Add(new SupportActionDefinition(
                SupportActionId.ElintSweep, "ELINT SWEEP",
                "Station SIGINT array locates enemy ground radars that are emitting.",
                SupportCapabilities.Recon, settings.ElintEnabled, new ElintAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.MtiSweep, "MTI SWEEP",
                "RADAR bird reveals moving ground contacts through a live uplink.",
                SupportCapabilities.Recon, settings.MtiEnabled, new MtiAction(),
                SpaceBirdRequirement.Radar, BirdTask.Mti, SpaceRevealWindow.BirdBusySeconds));

            actions.Add(new SupportActionDefinition(
                SupportActionId.SatCamera, "SAT CAMERA",
                "OPTICAL bird images the mark in daylight and reveals ground units in its window through a live uplink.",
                SupportCapabilities.Recon, settings.SatCameraEnabled, new SatelliteCameraAction(),
                SpaceBirdRequirement.Optical, BirdTask.Camera, SpaceRevealWindow.BirdBusySeconds));

            if (fortifications != null)
                actions.Add(new SupportActionDefinition(
                    SupportActionId.Fortify, "ZONE FORTIFICATION",
                    "Reinforce a controlled zone with defenders.",
                    SupportCapabilities.Fortify, settings.FortifyEnabled, new FortifyAction(fortifications)));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Artillery, "ORBITAL ROD",
                "KINETIC bird releases one physical rod through a live uplink.",
                SupportCapabilities.Artillery, settings.ArtilleryEnabled, new ArtilleryAction(),
                SpaceBirdRequirement.Kinetic, BirdTask.Rod, 14f, true));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Prsm, "PRSM STRIKE",
                "One offboard ballistic missile onto the mark. Needs fresh HQ intel at the target.",
                SupportCapabilities.Artillery, settings.PrsmEnabled, new PrsmAction(), SpaceBirdRequirement.OpticalOrRadar));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Cruise, "CRUISE SALVO",
                "Bounded salvo of offboard cruise missiles onto the mark, counted against the faction live cap.",
                SupportCapabilities.Artillery, settings.CruiseEnabled, new CruiseAction(), SpaceBirdRequirement.OpticalOrRadar));

            actions.Add(new SupportActionDefinition(
                SupportActionId.Emp, "EMP SHOCK",
                "30 s hostile radar blackout. Batteries widen the pulse; a banked SCREEN package adds up to 25% radius and duration.",
                SupportCapabilities.Emp, settings.EmpEnabled, new EmpAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.FlareMissile, "FLARE BARRAGE",
                "Airburst IR countermeasure cloud.",
                SupportCapabilities.Recon, settings.FlareBarrageEnabled, new FlareMissileAction()));

            actions.Add(new SupportActionDefinition(
                SupportActionId.JtacMark, "JTAC MARK",
                "SOF team lases the nearest hostile unit at the mark; friendly laser-guided weapons see it.",
                SupportCapabilities.Recon, null, new MarkAction()));
            actions.Add(new SupportActionDefinition(
                SupportActionId.JtacUnlase, "JTAC UNLASE",
                "Clears one lase reference on the nearest hostile unit at the mark.",
                SupportCapabilities.Recon, null, new UnlaseAction()));
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
