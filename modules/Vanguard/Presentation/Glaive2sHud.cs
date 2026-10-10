using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>GLAIVE-2S launcher HUD: one held line per drone on vg.glaive2s; it goes stale when the drone dies.</summary>
    internal static class Glaive2sHud
    {
        private const string Owner = "vanguard.glaive2s";
        private const string Channel = "vg.glaive2s";
        private static readonly string[] Ammo = new string[GlaiveProfile.Rounds + 1];

        public static void Show(string droneKey,GlaivePhase phase,int roundsLeft,float slide)
        {
            roundsLeft=System.Math.Max(0,System.Math.Min(roundsLeft,GlaiveProfile.Rounds));
            if (!ModuleServices.TryGet(out IHudBoard board)) return;
            VanguardHudFeeds.DeclareChannel(Channel,"SPECTRE");
            HudTone tone=phase == GlaivePhase.Depart ? (roundsLeft <= 0 ? HudTone.Warning : HudTone.Caution)
                : phase == GlaivePhase.Orbit && slide >= GlaiveProfile.HandOffShown ? HudTone.Caution : HudTone.Info;
            VanguardHudFeeds.Acquire(board, Owner,Channel,droneKey)?.Set(tone,GlaiveProfile.HudText(phase,roundsLeft,slide),
                Ammo[roundsLeft] ??= "30MM " + roundsLeft,roundsLeft/(float)GlaiveProfile.Rounds);
        }
    }
}
