using BoscaliSummer.Modules.Support.Presentation.Views;
using BoscaliSummer.Modules.Support.Presentation.Window;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The OPS window: one popup root per panel and the rooms it hosts. The MFD buttons that used to
    /// open a full-screen overlay open a room here instead. Created on first use, destroyed with the
    /// page on scene reset.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private OpsWindow opsWindow;
        private CrewOperationsView deskView;
        private CrewOperationsView cyberView;
        private CrewOperationsView taskingView;
        private StationView stationView;
        private ImagerView imagerView;

        private OpsWindow OpsWindowRoot()
        {
            if (opsWindow != null) return opsWindow;
            opsWindow = OpsWindow.Create(() => support != null && support.Settings != null && support.Settings.ReduceMotion.Value);
            opsWindow.Register(TaskingRoom());
            opsWindow.Register(StationRoom());
            opsWindow.Register(ImagerRoom());
            opsWindow.Register(CyberRoom());
            opsWindow.Register(DeskRoom());
            return opsWindow;
        }

        private CrewOperationsView DeskRoom() => deskView ?? (deskView = new CrewOperationsView(support,
            Domain.OpsDomain.SpecialOperations, specLoop, null, null, () => opsWindow?.Close()));
        private CrewOperationsView CyberRoom() => cyberView ?? (cyberView = new CrewOperationsView(support,
            Domain.OpsDomain.Cyber, cyberLoop, null, null, () => opsWindow?.Close()));
        private CrewOperationsView TaskingRoom() => taskingView ?? (taskingView = new CrewOperationsView(support,
            Domain.OpsDomain.Space, loop, () => OpenRoom(StationRoom(), null, null),
            () => OpenRoom(ImagerRoom(), null, null), () => opsWindow?.Close()));

        private StationView StationRoom() =>
            stationView ?? (stationView = new StationView(support, plan, loop,
                () => OpenRoom(ImagerRoom(), null, null), () => OpenRoom(TaskingRoom(), null, null)));

        private ImagerView ImagerRoom() =>
            imagerView ?? (imagerView = new ImagerView(support, products, () => OpenRoom(TaskingRoom(), null, null)));

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
            taskingView = null;
            stationView = null;
            imagerView = null;
        }
    }
}
