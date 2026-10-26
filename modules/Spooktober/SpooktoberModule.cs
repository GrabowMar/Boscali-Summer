using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Spooktober.Patches;
using BoscaliSummer.Modules.Spooktober.Presentation;
using BoscaliSummer.Modules.Spooktober.Runtime;

namespace BoscaliSummer.Modules.Spooktober
{
    /// <summary>SPOOKTOBER: a seasonal easter egg. A mystery transmission, puzzles hidden in SET and a secret boss menu
    /// (kaiju, fallen ace, redacted). Spec: docs/superpowers/specs/2026-10-19-spooktober-design.md.</summary>
    internal sealed class SpooktoberModule : IModule
    {
        // The FALLEN ACE's hull, its gun on enemy pilots, its pilot, bail-out, phantom and target choice (host; one reference
        // check while no ace is out).
        private static readonly Type[] Patches =
        {
            typeof(FallenAcePartDamagePatch), typeof(FallenAcePilotGunPatch), typeof(FallenAcePilotDamagePatch), typeof(FallenAceEjectionPatch),
            typeof(FallenAcePhantomPatch), typeof(FallenAceTargetPatch),
        };

        public ModuleMetadata Metadata { get; } = new ModuleMetadata("spooktober", "Spooktober");
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            SpookStore.Init(context.Settings.Spooktober);
            context.AddSceneService<SpookTransmission>(72).Configure(context.Logger);
            context.AddSceneService<SpookBosses>(73).Configure(context.Settings.Spooktober, context.Logger);
            context.AddSceneService<RadGhostWatcher>(74); // the rad look on the kaiju and the ace (every peer, local)
            context.AddSceneService<FallenAceDirector>(75);
            // Last: SET builds its PILOT console against this (puzzles, then the SPOOKTOBER tab). The kaiju card reads whether the kaiju service exists.
            context.AddService<IFallenAce>(new SpookAce());
            context.AddService<ISetExtras>(new SpookSetExtras(() => ModuleServices.TryGet(out IKaijuBoss _)));
            context.Logger.LogInfo("[Spooktober] Ready.");
        }
    }
}
