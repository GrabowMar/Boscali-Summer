using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Folds the panel column of the expanded map (2026-11-15): a drawer tab on the map's left edge. Folded, the column's width goes
    /// to the map (<see cref="MfdLayout.Resolve"/> lays it out at zero) and the shown panel is closed (so it stops refreshing too); the
    /// map's field log stays in its corner. The tab opens it again with the same panel; a rail key, or any docked screen being shown, opens it with that
    /// screen. Client-local, kept until the game closes.
    /// </summary>
    internal static class MfdColumnFold
    {
        private const string Name = "NOAvionics.ColumnFold";
        private const float HandleW = 18f, HandleH = 64f;

        private static RectTransform handle;
        private static AvControl button;
        private static MFDScreen stowed;

        internal static bool Folded { get; private set; }

        /// <summary>Builds or moves the tab to the map's left edge, half way down. Called with every layout pass.</summary>
        internal static void Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;
            if (handle == null || handle.parent != canvas.transform) Build(canvas);
            handle.anchorMin = handle.anchorMax = new Vector2(0.5f, 0.5f);
            handle.pivot = new Vector2(0f, 0.5f);
            handle.anchoredPosition = new Vector2(columns.Map.x, columns.Map.y - columns.Map.height * 0.5f);
            if (!handle.gameObject.activeSelf) handle.gameObject.SetActive(true);
            handle.SetAsLastSibling();
            button.SetIcon(Folded ? AvIcon.ChevronRight : AvIcon.ChevronLeft);
            button.Help = Folded ? "Open the panel column again (a rail key opens it with that panel)."
                : "Fold the panel column away and give its room to the map.";
        }

        private static void Build(Canvas canvas)
        {
            Reset();
            var go = new GameObject(Name, typeof(RectTransform));
            handle = (RectTransform)go.transform;
            handle.SetParent(canvas.transform, false);
            handle.sizeDelta = new Vector2(HandleW, HandleH);
            button = AvControl.Make(handle, new AvControl.Spec("", Toggle, AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            AvLay.Fill(button.Rect);
            button.Bracket = 0f;
            MapUiPointer.Register(handle);
        }

        internal static void Toggle()
        {
            if (Folded) Unfold(true);
            else Fold();
        }

        private static void Fold()
        {
            MFDScreen active = MfdSinglePanelPatch.ActiveScreen;
            stowed = active != null && active.isActive ? active : null;
            Folded = true;
            if (stowed != null) stowed.CloseScreen(Screen.width * Vector3.left);
            // Forget the selection, or the single-panel reconcile would show the stowed screen again.
            MfdSinglePanelPatch.Reset();
            MfdRailPatch.ReLayoutNow();
        }

        /// <summary>Opens the column; <paramref name="reopen"/> shows the panel it was folded with (the tab), false keeps whatever is opening it.</summary>
        internal static void Unfold(bool reopen)
        {
            if (!Folded) return;
            Folded = false;   // first: the ShowScreen below lands in OnScreenShown, which unfolds too
            MFDScreen back = reopen ? stowed : null;
            stowed = null;
            if (back != null && !back.isActive && MfdPanelDock.IsDocked(back))
            {
                back.ShowScreen(Vector3.zero);
                MfdSinglePanelPatch.Adopt(back);
            }
            MfdRailPatch.ReLayoutNow();
        }

        /// <summary>Map closed: the tab hides with the rest of the layout (the fold is kept).</summary>
        internal static void Hide()
        {
            if (handle != null && handle.gameObject.activeSelf) handle.gameObject.SetActive(false);
        }

        /// <summary>Scene reset: the tab goes; the fold is kept, the stowed screen is not.</summary>
        internal static void Reset()
        {
            if (handle != null)
            {
                MapUiPointer.Unregister(handle);
                Object.Destroy(handle.gameObject);
            }
            handle = null;
            button = null;
            stowed = null;
        }
    }
}
