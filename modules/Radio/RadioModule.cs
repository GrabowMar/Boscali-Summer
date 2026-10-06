using System;
using BoscaliSummer.Modules.Radio.Patches;
using BoscaliSummer.Modules.Radio.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;

namespace BoscaliSummer.Modules.Radio
{
    internal sealed class RadioModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("radio", "Radio");
        private static readonly Type[] Patches =
        {
            typeof(VanillaPlayMusicPatch),
            typeof(VanillaCrossFadeMusicPatch),
            typeof(VanillaQueueMusicPatch)
        };

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Patches;

        public void Install(ModuleContext context)
        {
            RadioManager radio = context.AddSceneService<RadioManager>(40);
            radio.Configure(context.Settings.Radio, context.Services);
            context.AddClientEffect(radio.ClientFx);
            context.AddService<IRadioRemote>(radio);
            context.AddSceneService<Presentation.RadioHudLine>(41).Configure(radio);
        }
    }
}
