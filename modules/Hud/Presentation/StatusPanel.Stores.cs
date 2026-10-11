using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The stores side of the board: the weapon (HERO), the stations, the countermeasures (EXPENDABLES) and the capacitor (PWR BUS), drawn by StoresHeader. What it has to say to the board: which of the three blocks
    // exist (BoardBlocks.Hero / Pwr / Exp from the header's flags), the surface the header paints on, and which native boxes it stands in for (TickNatives). Nothing here touches the wing side.
    internal sealed partial class StatusPanel
    {
        private readonly StoresHeader header = new StoresHeader();
        /// <summary>The header's last paint threw: its native boxes are shown again until a paint succeeds.</summary>
        private bool headerFault;

        /// <summary>The 10 Hz read of the stores feed; the THREAT page's countermeasure call says which dispenser pulses DISPENSE.</summary>
        private void PollStores() =>
            header.Poll(now, CuePriority.Has(allow.Voiced, ScopePage.Threat) ? ((ThreatPage)pages[(int)ScopePage.Threat]).CallMask : (byte)0);

        /// <summary>The header's own faults are reported once (its id sits past the pages').</summary>
        private void ReportHeaderOnce(Exception e)
        {
            if (failed.Add(CuePriority.PageCount)) BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Scope.StoresHeader", e);
        }

        /// <summary>
        /// The stores header on its own surface (board coordinates, from the top of the box). It is drawn as one piece at the left of the box over the HERO and SAT columns until the stores task redraws it as the three
        /// blocks. A header that throws is reported once and its native boxes come back.
        /// </summary>
        private void PaintStores(in ScopePalette pal, in BoardGeometry geo, bool flashOn)
        {
            float h = header.Height;
            if (h <= 0f) { Show(storesRect, false); header.Clear(); return; }
            Show(storesRect, true);
            float height = Whole((ScopeLayout.BlockTop + h) * unit);
            PlaceOnGrid(storesRect, 0f, 0f, Whole(geo.Width * unit), height);
            ScopeCanvas g = storesCanvas;
            g.Furniture = allow.Furniture;
            g.Begin(pal, unit, height, pixel);
            try
            {
                header.Paint(g, ScopeLayout.BlockTop, now, flashOn);
                headerFault = false;
            }
            catch (Exception e)
            {
                ReportHeaderOnce(e);
                header.Clear();
                headerFault = true;
            }
            g.End();
            storesVector.Commit();
        }

        /// <summary>
        /// Every frame, after <see cref="Ensure"/>: hide the native weapon, countermeasure and capacitor boxes whose replacement the header is drawing, show the rest (<see cref="NativeCorner"/>,
        /// <see cref="NativeTakeover"/>). Fail open: no board, a hidden or inactive board, a header that threw, a hidden HUD or a batch run all give the native boxes back this frame; and a
        /// board that has stopped being presented (its update threw: what it shows is frozen) is taken off the screen, so the native boxes show live data in its place.
        /// </summary>
        public void TickNatives()
        {
            bool stale = NativeTakeover.Stale(Time.unscaledTime, presentedAt);
            if (stale && root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);      // Repaint shows it again when Present resumes
            if (corner == null) return;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool alive = root != null && hasBoard && !stale && !headerFault && root.gameObject.activeInHierarchy;
            bool hudUp = hud != null && hud.iconLayer != null && hud.iconLayer.gameObject.activeInHierarchy, batch = Application.isBatchMode;
            corner.Apply(NativeTakeover.Hide(alive, true, hudUp, batch, header.WeaponDrawn), NativeTakeover.Hide(alive, true, hudUp, batch, header.CmDrawn), NativeTakeover.Hide(alive, true, hudUp, batch, header.CapDrawn));
        }
    }
}
