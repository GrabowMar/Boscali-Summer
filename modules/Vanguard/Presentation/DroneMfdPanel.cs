using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// UAV: the VANGUARD drone link on the maximised-map MFD (host slot, left column). A brief summary in the OPS layout the owner ruled
    /// for: the linked drone's live feed as the hero, the asset list (click = link), TAKE CONTROL / NEXT and the button that opens the
    /// UAV TERMINAL. Every press goes to <see cref="DroneLinkService"/>.
    /// </summary>
    internal sealed class DroneMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth, RefreshInterval = 0.25f;
        internal const string Title = "VANGUARD UAV LINK";

        private DroneLinkService link;
        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Uav, "UAV", "BoscaliUav.Screen", preferLeft: true) { Host = true, Wrap = true, Clamp = false };
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private AvChip[] chips;
        private DroneFeedView feed;
        private AvList list;
        private AvControl open, hand, next;
        private float nextRefresh;

        /// <summary>The scene's panel (the sim hook presses its bezel).</summary>
        internal static DroneMfdPanel Active { get; private set; }
        internal bool Showing => screen != null && screen.isActive;

        public void Configure(DroneLinkService service, ManualLogSource log)
        {
            Active = this;
            link = service;
            installer.Log = log;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
            console = null;
            chips = null;
            feed = null;
            list = null;
            nextRefresh = 0f;
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            if (link == null || installer.Failed || !installer.Tick()) return;
            bool visible = screen.isActive && SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            if (!visible || console == null) return;
            feed.Paint(link);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        /// <summary>Sim seam: shows the UAV screen the way its bezel button does.</summary>
        internal void OpenForAutomation()
        {
            if (screen != null && !screen.isActive && installer.Bezel != null) installer.Bezel.onClick.Invoke();
            nextRefresh = 0f;
        }

        private RectTransform BuildScreen(RectTransform content, float height) => Build(content, height);

        /// <summary>The console under <paramref name="content"/>; the offline harness builds it the same way.</summary>
        internal RectTransform Build(RectTransform content, float height)
        {
            console = AvConsole.Build(content, "UAV", Title, 1, Width, height);
            chips = console.Chips(3);
            AvFlow page = console.Page(0);
            page.Section(AvIcon.Camera, "LIVE FEED", "LINKED DRONE");
            feed = page.Add(new DroneFeedView(page.Content, "Feed", 11f));
            AvButtons row = page.Buttons(
                new AvControl.Spec("UAV TERMINAL", () => link.OpenTerminal(), AvButtonStyle.Primary, AvIcon.Terminal),
                new AvControl.Spec("TAKE CONTROL", () => { link.ToggleHand(); Refresh(); }, AvButtonStyle.Default, AvIcon.Drone),
                new AvControl.Spec("NEXT", () => { link.Cycle(1); Refresh(); }, AvButtonStyle.Default, AvIcon.ChevronRight));
            open = row.Controls[0];
            hand = row.Controls[1];
            next = row.Controls[2];
            open.Help = "Open the UAV terminal: every drone on the link, the live feed, the tactical plot and waypoints.";
            hand.Help = "Fly the linked drone by hand: it goes the way your nose points.";
            next.Help = "Link the next drone; while you hand-fly, the stick goes with the link.";
            page.Section(AvIcon.Drone, "ASSETS ON LINK", "CLICK TO LINK");
            list = page.Add(new AvList(page.Content, console.Ticker, 5, BindRow) { RowClicked = i => { link.Link(i); Refresh(); } });
            console.Orders(0,
                "ONE DRONE ON THE STICK AT A TIME: NEXT HANDS IT OVER",
                "IN FLIGHT: C › WEAPONS › DRONE LINK",
                "LINK DROPS PAST " + AvNum.Fixed(DroneLink.MaxRangeM / 1000f, 0) + " KM: THE DRONE RETURNS TO TASK");
            console.Finish();
            console.SetPage(0);
            return null;
        }

        internal void Refresh()
        {
            if (console == null) return;
            DroneView v = link.Linked;
            chips[0].Set(v != null ? "LINK · " + v.Callsign : link.Count > 0 ? "LINK · NONE" : "NO DRONE AIRBORNE", v != null ? AvState.Info : AvState.Inert);
            chips[1].Set(v != null ? "ORDER · " + DroneLink.Word(v.Mode) : "ORDER · —",
                v == null ? AvState.Inert : v.Mode == DroneMode.Hand ? AvState.Caution : AvState.Ready);
            chips[2].Set(link.Count + " ON NET", link.Count > 0 ? AvState.Ready : AvState.Inert);
            list.SetCount(link.Count);
            hand.Label = v != null && v.Mode == DroneMode.Hand ? "RELEASE" : "TAKE CONTROL";
            hand.Latched = v != null && v.Mode == DroneMode.Hand;
            hand.Interactable = link.Count > 0;
            next.Interactable = link.Count > 1;
            console.Footer.Set(v == null ? (link.Count > 0 ? "PICK A DRONE TO LINK IT" : "LAUNCH A VANGUARD DRONE TO PUT IT ON THE LINK")
                : v.Callsign + " · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM · LINK " + AvNum.Fixed(v.Quality * 100f, 0) + "%",
                v != null && v.Quality < 0.35f ? AvState.Caution : AvState.Inert);
        }

        private void BindRow(int index, AvRow row)
        {
            if (index < 0 || index >= link.Count) { row.Set("—", "", ""); return; }
            DroneView v = link[index];
            bool linked = index == link.LinkedIndex;
            row.Set(v.Callsign + (linked ? "  [LINK]" : ""), v.Job + " · " + DroneLink.Word(v.Mode), AvNum.Fixed(v.RangeM / 1000f, 1) + " KM",
                v.Quality <= 0f ? AvState.Danger : v.Mode == DroneMode.Hand ? AvState.Caution : linked ? AvState.Info : AvState.Ready);
            row.Help = "Link " + v.Callsign + ".";
        }
    }
}
