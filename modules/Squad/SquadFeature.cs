using System;
using BoscaliSummer.Features.Squad.Networking;
using BoscaliSummer.Features.Squad.Patches;
using BoscaliSummer.Features.Squad.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Features.Squad
{
    internal sealed class SquadFeature : IModFeature
    {
        public FeatureMetadata Metadata => new FeatureMetadata("squad", "Squad / ace hunts");
        public Type[] PatchTypes => new[] { typeof(SquadDamagePatch), typeof(SquadKillPatch), typeof(SquadPilotDeathPatch) };
        public void Install(FeatureContext context)
        {
            SquadManager manager = context.AddSceneService<SquadManager>(44);
            SquadNet network = context.AddComponent<SquadNet>();
            manager.Configure(context.Settings.Squad, network, context.Logger, context.Services); network.Configure(manager);
            context.AddService<ISquadView>(manager);
            context.Logger.LogInfo("[Squad] Wing Command API: " + (WingLink.SquadAvailable ? "ready" : WingLink.SquadUnavailableReason));

            // Whether a dead pilot stays dead, and whether aces come looking. What it takes
            // to provoke a hunt and how long the quiet after one lasts are balance, and stay
            // in the config file.
            context.AddHostSettings(new HostSettingsTable("SQUAD AND ACES")
                .Choice(1, context.Settings.Squad.PilotLives, "PILOT CAREER",
                    "Respawning keeps the generated pilot and perks; One Life retires a confirmed dead pilot. Ejection alone is not death.")
                .Toggle(2, context.Settings.Squad.EnemyAceHunts, "ACE HUNTS",
                    "Enemy aces lead escalating wings that hunt a player after hostile damage.",
                    () => WingLink.SquadAvailable ? null : WingLink.SquadUnavailableReason));
        }
    }
}
