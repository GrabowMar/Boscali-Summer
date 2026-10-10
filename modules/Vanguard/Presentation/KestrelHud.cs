using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>KS-01 KESTREL launcher HUD: one held line per glider on vg.kestrel; it goes stale when the glider is gone.</summary>
    internal static class KestrelHud
    {
        private const string Owner = "vanguard.kestrel";
        private const string Channel = "vg.kestrel";

        /// <summary>State and station clock; <paramref name="detail"/> is KestrelProfile.HudDetail (spotted count, lased target).</summary>
        public static void Show(string droneKey, KestrelPhase phase, float stationLeft, string detail)
        {
            if (!ModuleServices.TryGet(out IHudBoard board)) return;
            board.DeclareChannel(Channel, "KESTREL");
            board.Acquire(Owner, Channel, droneKey)?.Set(phase == KestrelPhase.Depart ? HudTone.Caution : HudTone.Info,
                KestrelProfile.HudText(phase, stationLeft), detail, stationLeft / KestrelProfile.StationSeconds);
        }
    }
}
