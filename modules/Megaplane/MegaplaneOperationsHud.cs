using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Megaplane
{
    internal sealed class MegaplaneOperationsHud : HudLineWidget
    {
        private string text, detail;
        private float progress;
        private HudTone tone;

        protected override string Owner => "groza-bay-operations";
        protected override string ChannelKey => "sys";

        protected override bool WantsLine()
        {
            progress = 0f;
            tone = HudTone.Info;
            if (!GameManager.GetLocalAircraft(out Aircraft carrier) || carrier == null || carrier.disabled) return false;
            if (carrier.TryGetComponent(out MegaplaneRailgun rail)) return Trident(rail);
            if (!MegaplaneBayControls.TryBay(carrier, out var bay, out var operations)) return false;
            FighterBayPhase front = operations.Phase(bay, 0), aft = operations.Phase(bay, 1);
            if (!InProgress(front) && !InProgress(aft)) return false;
            bool approach = MegaplaneBayControls.Approaching(front) || MegaplaneBayControls.Approaching(aft);
            string reason = approach ? bay.DeckHoldReason : null;
            bool unstable = reason != null;
            tone = unstable || front == FighterBayPhase.Clearing || aft == FighterBayPhase.Clearing ? HudTone.Caution : HudTone.Info;
            text = reason ?? "FIGHTER BAY";
            detail = MegaplaneBayControls.Read(bay, operations).Slots;
            return true;
        }

        private bool Trident(MegaplaneRailgun rail)
        {
            if (!rail.Selected && rail.Phase == RailgunPhase.Stowed) return false;
            WeaponStation station = rail.Turret != null ? rail.Turret.GetWeaponStation() : null;
            progress = rail.CycleProgress;
            text = "TRIDENT " + (rail.Phase switch
            {
                RailgunPhase.Opening => "BAY OPENING",
                RailgunPhase.Lowering => "LOWERING",
                RailgunPhase.Homing => "CENTERING",
                RailgunPhase.Raising => "RAISING",
                RailgunPhase.Closing => "BAY CLOSING",
                RailgunPhase.Deployed => station == null || station.Ammo == 0 ? "EMPTY" : rail.Charge < 1f ? "RECHARGING" : "READY",
                _ => "STOWED",
            });
            if (rail.Phase == RailgunPhase.Deployed) progress = rail.Charge;
            if (!rail.Live || rail.Charge < 1f || station == null || station.Ammo <= 0) tone = HudTone.Caution;
            detail = station == null ? null : station.Ammo + " ROUNDS";
            return true;
        }

        private static bool InProgress(FighterBayPhase phase) => phase == FighterBayPhase.Servicing ||
            phase == FighterBayPhase.Launching || phase >= FighterBayPhase.Marshal;

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, progress);
    }
}
