using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Nation.Configuration;
using BoscaliSummer.Modules.Nation.Networking;
using BoscaliSummer.Modules.Nation.Patches;
using BoscaliSummer.Modules.Nation.Runtime;

namespace BoscaliSummer.Modules.Nation
{
    /// <summary>Factories, policies and the focus tree over the vanilla economy. Rules: <c>modules/Nation/AGENTS.md</c>.</summary>
    internal sealed class NationModule : IModule
    {
        private static readonly ModuleMetadata Module = new ModuleMetadata("nation", "Factories, policies and the national focus tree");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => new[] { typeof(NationFactoryPatch) };

        public void Install(ModuleContext context)
        {
            NationSettings settings = context.Settings.Nation;
            NationNet network = context.AddComponent<NationNet>();
            NationService service = context.AddSceneService<NationService>(47);
            service.Configure(settings, network);
            network.Configure(service);
            context.AddService<INationView>(service);
            context.AddService<INationModifiers>(service);

            context.AddHostSettings(new HostSettingsTable("NATION")
                .Toggle(1, settings.Enabled, "FACTORIES AND FOCUS TREE",
                    "Run the nation layer in Escalation, Terminal Control and Domination. Factories produce the stock; each faction has three policies and a focus tree. The staff runs it when nobody does.")
                .Number(2, settings.StartingStock, "STARTING STOCK",
                    "Share of the mission's own aircraft and vehicle stock a faction starts with; the rest is produced. Read at mission start.", 0.05f,
                    value => Math.Round(value * 100f) + " %")
                .Number(3, settings.FactoryCost, "FACTORY COST", "Faction funds a new factory costs.", 500)
                .Number(4, settings.FocusSeconds, "FOCUS TIME", "Seconds one national focus takes.", 30, value => value + " S"));
        }
    }

    /// <summary>Sim hook (tests/ingame/nation-*.json): reads every faction's nation and can press a verb as the local player.</summary>
    internal static class NationProbe
    {
        public static Dictionary<string, object> Read(Dictionary<string, object> args) => NationService.Probe(args);
    }
}
