using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// HAMILCAR launcher HUD on vg.hamilcar: one held line per glider while it flies (it goes stale once the caller stops
    /// setting it) and the DOWN / SHORT / LOST notices. The words live in HamilcarProfile; HamilcarVisuals decides when.
    /// </summary>
    internal static class HamilcarHud
    {
        private const string Owner = "vanguard.hamilcar";
        private const string Channel = "vg.hamilcar";

        public static void Held(string gliderKey, float metres, HamilcarPhase phase)
        {
            if (Board(out IHudBoard board))
                VanguardHudFeeds.Acquire(board, Owner, Channel, gliderKey)?.Set(HudTone.Info, HamilcarProfile.HudText(metres), HamilcarProfile.HudDetail(phase), 0f);
        }

        public static void Notice(HudTone tone, string text, string detail)
        {
            if (Board(out IHudBoard board)) board.Notice(Channel, tone, text, detail);
        }

        private static bool Board(out IHudBoard board)
        {
            if (!ModuleServices.TryGet(out board)) return false;
            VanguardHudFeeds.DeclareChannel(Channel, "HAMILCAR");
            return true;
        }
    }
}
