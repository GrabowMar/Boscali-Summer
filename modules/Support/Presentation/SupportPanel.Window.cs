using BoscaliSummer.Features.Support.Presentation.Views;
using BoscaliSummer.Features.Support.Presentation.Window;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The OPS window: one popup root per panel and the rooms it hosts. The MFD buttons that used to
    /// open a full-screen overlay open a room here instead. Created on first use, destroyed with the
    /// page on scene reset.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private OpsWindow opsWindow;
        private DeskView deskView;
        private CyberView cyberView;
        private StationView stationView;
        private ImagerView imagerView;

        private OpsWindow OpsWindowRoot()
        {
            if (opsWindow != null) return opsWindow;
            opsWindow = OpsWindow.Create(() => support != null && support.Settings != null && support.Settings.ReduceMotion.Value);
            opsWindow.Register(StationRoom());
            opsWindow.Register(ImagerRoom());
            opsWindow.Register(CyberRoom());
            opsWindow.Register(DeskRoom());
            return opsWindow;
        }

        private DeskView DeskRoom() => deskView ?? (deskView = new DeskView(support, specLoop));

        private CyberView CyberRoom() => cyberView ?? (cyberView = new CyberView(support, cyberLoop));

        private StationView StationRoom() =>
            stationView ?? (stationView = new StationView(support, plan, loop, () => OpenRoom(ImagerRoom(), null, null)));

        private ImagerView ImagerRoom() =>
            imagerView ?? (imagerView = new ImagerView(support, products, () => OpenRoom(StationRoom(), null, null)));

        /// <summary>True while the imager is on screen; the SAR scene forms only while it, or the MFD, is.</summary>
        private bool ImagerOpen => imagerView != null && imagerView.Active && Window.OpsWindow.IsOpen;

        /// <summary>Open a room from the control that asked, so the window grows out of it.</summary>
        private void OpenRoom(IOpsView room, object context, Component from)
        {
            if (room == null || support == null) return;
            OpsWindowRoot().Show(room, context, from != null ? from.transform as RectTransform : null);
        }

        private void ResetOpsWindow()
        {
            if (opsWindow != null) Destroy(opsWindow.gameObject);
            opsWindow = null;
            deskView = null;
            cyberView = null;
            stationView = null;
            imagerView = null;
        }
    }
}
