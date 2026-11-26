using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Chatter.Runtime;

namespace BoscaliSummer.Modules.Chatter
{
    /// <summary>The radio: the chatter packs, the one channel every net shares, the HUD strip and the command net. Publishers reach it through <see cref="IChatter"/>.</summary>
    internal sealed class ChatterModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("chatter", "Chatter");
        private static readonly Type[] Patches =
        {
            typeof(BoscaliSummer.Modules.Chatter.Patches.ChatterKillPatch),
            typeof(BoscaliSummer.Modules.Chatter.Patches.ChatterShipPatch),
            typeof(BoscaliSummer.Modules.Chatter.Patches.ChatterNoticePatch),
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var chatter = context.Settings.Chatter;
            ChatterHost host = context.AddSceneService<ChatterHost>(35);
            host.Configure(context.Settings.Wing, context.Logger, chatter.Allows);
            context.AddService<IChatter>(host);

            const string Comms = "RADIO COMMS";
            context.AddClientSetting(Comms, "AWACS COMMS",
                "Play AWACS (SKYWATCH) traffic: radar contact, bandits, missiles, SAM launches, kills and fuel/gear reminders.",
                chatter.AwacsEnabled);
            context.AddClientSetting(Comms, "GROUND FORCES",
                "Play ground forces and paratrooper traffic: base captures, withdrawals and air assault insertions.",
                chatter.GroundForcesEnabled);
            context.AddClientSetting(Comms, "NAVAL COMMS",
                "Play naval traffic: carrier evacuation orders and friendly ship loss reports.",
                chatter.NavalEnabled);
            context.AddClientSetting(Comms, "COMMAND & HQ",
                "Play theater command traffic: world events, operations, OPS perks, contracts, drones and support control.",
                chatter.CommandEnabled);
            context.AddClientSetting(Comms, "AIRFIELD TOWER",
                "Play airfield tower traffic: startup, taxi, landing clearances, takeoff calls and airfield attack alarms.",
                chatter.TowerEnabled);
            context.AddClientSetting(Comms, "WINGMEN COMMS",
                "Play wingman traffic: order acknowledgements, weapon calls, kills, damage and flight banter.",
                chatter.WingmenEnabled);
            context.AddClientSetting(Comms, "ALLIED FLIGHTS",
                "Play allied AI flight traffic: nearby friendly kills, ejections and thanks.",
                chatter.AlliedFlightsEnabled);
            context.AddClientSetting(Comms, "ENEMY SIGINT",
                "Play intercepted enemy pilot transmissions (SIGINT).",
                chatter.EnemySigintEnabled);
        }
    }
}
