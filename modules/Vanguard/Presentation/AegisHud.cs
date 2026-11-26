using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    internal static class AegisHud
    {
        private const string Channel = "vg.aegis";

        public static void Cue(Unit launcher, AegisCue cue, float rangeM = 0f)
        {
            if (launcher == null || !GameManager.GetLocalAircraft(out Aircraft me) || me != launcher || me.disabled) return;

            var (tone, title, detail) = cue switch
            {
                AegisCue.Intercept => (HudTone.Info, "AEGIS AWAY", rangeM > 0f ? AegisSnap.Km(rangeM) + " km" : null),
                AegisCue.Destroyed => (HudTone.Info, "AEGIS INTERCEPT", "MISSILE DESTROYED"),
                AegisCue.NoThreat => (HudTone.Caution, "AEGIS NO THREAT", null),
                AegisCue.Empty => (HudTone.Caution, "AEGIS EMPTY", null),
                AegisCue.SnapHit => (HudTone.Info, "AEGIS HIT", null),
                _ => (HudTone.Info, null, null),
            };
            if (title == null) return;
            if (ModuleServices.TryGet(out IHudBoard board) && board.TryGetScreenBox(out _, out _, out _, out _))
                board.Notice(Channel, tone, title, detail);
            else if (SceneSingleton<AircraftActionsReport>.i != null)
                SceneSingleton<AircraftActionsReport>.i.ReportText(detail == null ? title : title + " · " + detail, 3f);
        }
    }
}
