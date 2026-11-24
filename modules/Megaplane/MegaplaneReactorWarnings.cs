using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Megaplane
{
    internal static class MegaplaneReactorWarnings
    {
        internal static void Report(Aircraft carrier, byte previous, byte current)
        {
            if (!GameManager.GetLocalPlayer(out Player pilot) || pilot == null || pilot.Aircraft != carrier ||
                !ModuleServices.TryGet<IHudBoard>(out var board)) return;

            if (current == MegaplaneReactor.Flash)
                board.Notice("sys", HudTone.Warning, "REACTOR CONTAINMENT LOST");
            else if (current < MegaplaneReactor.Flash && current >= 7 && previous < 7)
                board.Notice("sys", HudTone.Warning, "REACTOR TEMPERATURE CRITICAL");
            else if (current >= 2 && current < 7 && previous < 2)
                board.Notice("sys", HudTone.Caution, "REACTOR TEMPERATURE RISING", "CHECK COOLING");
            else if (current == 0 && previous > 0)
                board.Notice("sys", HudTone.Info, "REACTOR TEMPERATURE STABLE");
        }
    }
}
