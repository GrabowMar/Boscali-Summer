using BepInEx.Logging;
using BoscaliSummer.Features.FireAndDestruction;
using BoscaliSummer.Features.Radio;
using BoscaliSummer.Features.UrbanCombat;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Infrastructure.Networking;
using BoscaliSummer.Runtime;

namespace BoscaliSummer.Bootstrap
{
    internal static class ModCompositionRoot
    {
        public static FeatureHost Start(ManualLogSource logger, ModConfiguration settings)
        {
            GameAccess.Initialise();
            var host = new FeatureHost(logger, settings);
            try
            {
                host.Load(new IModFeature[]
                {
                    new NetworkingFeature(),
                    new FireAndDestructionFeature(),
                    new UrbanCombatFeature(),
                    new RadioFeature()
                });
                CapabilityReport.Log();
                return host;
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }
    }
}
