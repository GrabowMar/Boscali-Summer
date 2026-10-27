using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Patches;
using BoscaliSummer.Modules.Spooktober.Presentation;
using BoscaliSummer.Modules.Spooktober.Runtime;

namespace BoscaliSummer.Modules.Spooktober
{
    /// <summary>SPOOKTOBER: a seasonal easter egg. A boss menu on MISC (kaiju, fallen ace, Hell's Army).</summary>
    internal sealed class SpooktoberModule : IModule
    {
        // The FALLEN ACE's hull, its gun on enemy pilots, its pilot, bail-out, phantom and target choice (host; one reference
        // check while no ace is out).
        private static readonly Type[] Patches =
        {
            typeof(FallenAcePartDamagePatch), typeof(FallenAcePilotGunPatch), typeof(FallenAcePilotDamagePatch), typeof(FallenAceEjectionPatch),
            typeof(FallenAcePhantomPatch), typeof(FallenAceTargetPatch),
            typeof(HellMapColourPatch), // Hell's Army is black on the map (one count check while it is empty)
        };

        public ModuleMetadata Metadata { get; } = new ModuleMetadata("spooktober", "Spooktober");
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            SpookStore.Init(context.Settings.Spooktober);
            context.AddSceneService<SpookBosses>(73).Configure(context.Settings.Spooktober, context.Logger);
            context.AddSceneService<RadGhostWatcher>(74); // the rad look on the kaiju and the ace (every peer, local)
            context.AddSceneService<FallenAceDirector>(75);
            context.AddSceneService<HellPortalMapLayer>(77); // the HELLGATE sign on the map (every peer, local)
            context.AddSceneService<HellGateDirector>(78);   // Hell's Army: waves, orders, tracks, fly-through (host)
            // Last: MISC builds its SPOOKTOBER tab against this. The kaiju card reads whether the kaiju service exists.
            context.AddService<IFallenAce>(new SpookAce());
            context.AddService<ISetExtras>(new SpookSetExtras(() => ModuleServices.TryGet(out IKaijuBoss _)));
            context.Logger.LogInfo("[Spooktober] Ready.");
        }
    }
}
