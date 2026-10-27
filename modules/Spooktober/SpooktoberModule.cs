using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Kaiju;
using BoscaliSummer.Modules.Spooktober.Kaiju.Networking;
using BoscaliSummer.Modules.Spooktober.Kaiju.Runtime;
using BoscaliSummer.Modules.Spooktober.Patches;
using BoscaliSummer.Modules.Spooktober.Presentation;
using BoscaliSummer.Modules.Spooktober.Runtime;

namespace BoscaliSummer.Modules.Spooktober
{
    /// <summary>SPOOKTOBER: a seasonal easter egg. A boss menu on MISC (kaiju, fallen ace, Hell's Army); the kaiju lives in Kaiju/.</summary>
    internal sealed class SpooktoberModule : IModule
    {
        // The FALLEN ACE's hull, its gun on enemy pilots, its pilot, bail-out, phantom and target choice (host; one reference
        // check while no ace is out).
        private static readonly Type[] Patches =
        {
            typeof(FallenAcePartDamagePatch), typeof(FallenAcePilotGunPatch), typeof(FallenAcePilotDamagePatch), typeof(FallenAceEjectionPatch),
            typeof(FallenAcePhantomPatch), typeof(FallenAceTargetPatch),
            typeof(KaijuPrefabPatch), typeof(KaijuSelfDamagePatch), // the kaiju (when it is on)
            typeof(HellMapColourPatch), // Hell's Army is black on the map (one count check while it is empty)
        };

        public ModuleMetadata Metadata { get; } = new ModuleMetadata("spooktober", "Spooktober");
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            if (context.Settings.Kaiju.Enabled.Value) InstallKaiju(context);
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

        /// <summary>KAIJU: a mountain-sized creature the host summons and every aircraft fights (F1 > Kaiju, or the boss menu).
        /// Spec: docs/superpowers/specs/2026-10-19-kaiju-design.md; art pipeline: Kaiju/Assets. Services come before the
        /// boss menu's so it finds IKaijuBoss.</summary>
        private static void InstallKaiju(ModuleContext context)
        {
            context.AddSceneService<KaijuNet>(70);
            KaijuDirector director = context.AddSceneService<KaijuDirector>(71);
            director.Configure(context.Settings.Kaiju);
            context.AddService<IKaijuBoss>(director);
            context.Logger.LogInfo("[Kaiju] Ready: the host summons it from F1 > Kaiju. It rises out of the ground or sea, marches on the nearest airbase, throws heat-seeking spikes, sweeps a lagging mouth beam and claws close aircraft. 21 glowing weak spots are lockable game units; breaking them takes attacks away, and enough of them expose the heart.");
        }
    }
}
