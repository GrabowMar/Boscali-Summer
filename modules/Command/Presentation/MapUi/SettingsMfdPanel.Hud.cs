using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildHudPage(RectTransform parent, Rect body)
        {
            ModServices.TryGet(out IHudBoard board);
            int feeds = board == null ? 1 : Mathf.Min(board.Channels.Count, HudLayout.MaxChannels);
            parent = Page(4, parent, body, HudSettingRows + feeds, 1, out var area);
            Heading(parent, ref area, "01", "STATUS & NOTICES", "LOCAL DISPLAY");
            BuildHudRows(parent, ref area, board);
        }

        private void BuildFlightStyleRows(RectTransform parent, ref Rect area, IThirdPersonHud hud)
        {
            Func<bool> available = () => hud != null && hud.IsEnabled && hud.ModifyVanillaHud;
            Stepper(parent, TakeRow(ref area), "FLIGHT SIZE", () => HudLayout.ScaleName(hud?.FlightScaleStep ?? 1),
                d => { if (hud != null) hud.FlightScaleStep = HudLayout.Cycle(hud.FlightScaleStep, 4, d); },
                available, available, "Scale flight readouts independently from the tactical dock.", available);
            Stepper(parent, TakeRow(ref area), "FLIGHT OPACITY", () => HudLayout.OpacityName(hud?.FlightOpacityStep ?? 0),
                d => { if (hud != null) hud.FlightOpacityStep = HudLayout.Cycle(hud.FlightOpacityStep, 4, d); },
                available, available, "Flight readout transparency; OFF leaves native tactical markers visible.", available);
            Stepper(parent, TakeRow(ref area), "FLIGHT CONTRAST", () => HudLayout.ContrastName(hud?.FlightContrast ?? 1),
                d => { if (hud != null) hud.FlightContrast = HudLayout.Cycle(hud.FlightContrast, 3, d); },
                available, available, "CLEAR ink, GLASS backing, or SOLID backing.", available);
        }

        private void BuildInstrumentRows(RectTransform parent, ref Rect area, IThirdPersonHud hud)
        {
            Func<bool> available = () => hud != null;
            Func<bool> on = () => hud != null && hud.BoardEnabled;
            Func<string> off = () => "Enable the instrument board first.";
            Toggle(parent, TakeRow(ref area), "INSTRUMENT BOARD", "Camera, systems, missile tracks and observations in external view.",
                () => hud != null && hud.BoardEnabled, v => { if (hud != null) hud.BoardEnabled = v; }, available);
            Toggle(parent, TakeRow(ref area), "SYSTEMS", "Counts of damaged parts, detached parts and native faults. No artificial health score.",
                () => hud != null && hud.AirframeEnabled, v => { if (hud != null) hud.AirframeEnabled = v; }, on, off);
            Toggle(parent, TakeRow(ref area), "MISSILE TRACKS", "Own shots and detected inbound warnings, up to three each.",
                () => hud != null && hud.ShotsEnabled, v => { if (hud != null) hud.ShotsEnabled = v; }, on, off);
            Toggle(parent, TakeRow(ref area), "OBSERVATION", "Range and age of your current camera mark.",
                () => hud != null && hud.MarkEnabled, v => { if (hud != null) hud.MarkEnabled = v; }, on, off);
            Stepper(parent, TakeRow(ref area), "BOARD CORNER", () => HudLayout.CornerName(hud?.BoardCorner ?? 0),
                d => { if (hud != null) hud.BoardCorner = HudLayout.Cycle(hud.BoardCorner, 4, d); },
                available, available, "Anchor to a safe-area corner; top corners leave room for native instruments.", on, off);
            Stepper(parent, TakeRow(ref area), "BOARD SIZE", () => HudLayout.ScaleName(hud?.BoardScaleStep ?? 1),
                d => { if (hud != null) hud.BoardScaleStep = HudLayout.Cycle(hud.BoardScaleStep, 4, d); },
                available, available, "Scales all board instruments together.", on, off);
            Stepper(parent, TakeRow(ref area), "BOARD OPACITY", () => HudLayout.OpacityName(hud?.BoardOpacityStep ?? 0),
                d => { if (hud != null) hud.BoardOpacityStep = HudLayout.Cycle(hud.BoardOpacityStep, 4, d); },
                available, available, "Transparency of the complete instrument board.", on, off);
            Stepper(parent, TakeRow(ref area), "BOARD CONTRAST", () => HudLayout.ContrastName(hud?.BoardContrast ?? 1),
                d => { if (hud != null) hud.BoardContrast = HudLayout.Cycle(hud.BoardContrast, 3, d); },
                available, available, "CLEAR floating ink, GLASS backing, or SOLID for bright sky.", on, off);
            HudOffset(parent, ref area, "INSET HORIZONTAL", () => hud?.BoardInsetX ?? 0,
                v => { if (hud != null) hud.BoardInsetX = v; }, 0, on, off);
            HudOffset(parent, ref area, "INSET VERTICAL", () => hud?.BoardInsetY ?? 0,
                v => { if (hud != null) hud.BoardInsetY = v; }, 0, on, off);
            AvStyled.Button(parent, TakeRow(ref area), "RESET INSTRUMENT LAYOUT", "btn", () =>
            { hud?.ResetLayout(); Echo("Instrument layout restored."); Changed(); });
        }

        private void HudOffset(RectTransform parent, ref Rect area, string label, Func<int> read,
            Action<int> write, int min, Func<bool> enabled, Func<string> reason)
        {
            Stepper(parent, TakeRow(ref area), label, () => read() + " px",
                d => write(Mathf.Clamp(read() + d * 20, min, 600)),
                () => read() > min, () => read() < 600,
                "Adjust by 20 reference pixels. The complete overlay is clamped inside the safe area.", enabled, reason);
        }
    }
}
