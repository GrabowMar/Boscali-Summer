using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal sealed class DroneMfdPanel : MonoBehaviour, ISceneService
    {
        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Drones, "DRN", "BoscaliDrones.Screen", preferLeft: false) { Host = true };
        private readonly Dictionary<int, Unit> rosterTracks = new Dictionary<int, Unit>();
        private AvConsole console;
        private AvSection rosterSection, feedSection;
        private AvList roster;
        private AvNote selectionHint;
        private AvButtons flight, optics;
        private AvControl locate, pilot, share, look, zoom, hud;
        private FeedPicture feed;
        private int lastSelection = -1;

        internal static int IndexOf(IDroneCommand drones, Unit track)
        {
            if (drones == null || track == null || track.disabled) return -1;
            for (int i = 0; i < drones.Linkable; i++)
                if (drones.Track(i) == track) return i;
            return -1;
        }

        internal static void OpenTrack(Unit track)
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones)) return;
            int index = IndexOf(drones, track);
            if (index < 0) return;
            drones.Link(index);
            AvConsole.Open(MfdSlots.Drones, "DRONES");
        }

        internal static bool OpenBezel()
        {
            if (!DynamicMap.mapMaximized) return false;
            VirtualMFD mfd = MfdPanelInstaller.ResolveMfd();
            if (mfd == null) return false;
            List<MFDScreen> screens = MapUiAccess.GetRightScreens(mfd);
            List<Button> buttons = MapUiAccess.GetRightButtons(mfd);
            if (screens == null || buttons == null) return false;
            for (int i = 0; i < screens.Count && i < buttons.Count; i++)
            {
                MFDScreen screen = screens[i];
                Button button = buttons[i];
                if (screen == null || screen.shortName != MfdSlots.Drones || button == null) continue;
                if (!screen.isActive) button.onClick.Invoke();
                return screen.isActive;
            }
            return false;
        }

        public void Configure(ManualLogSource log)
        {
            installer.Log = log;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
            feed?.Dispose();
            feed = null;
            console = null;
            lastSelection = -1;
            rosterTracks.Clear();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones))
            {
                if (installer.Screen != null || installer.Failed) ResetForScene();
                return;
            }
            if (!installer.Tick() || installer.Bezel == null) return;
            installer.Bezel.enabled = installer.Bezel.interactable = true;
            if (console != null && console.Root.gameObject.activeInHierarchy && feed.Shown)
                feed.Show(drones.WantFeed(), drones.LookWord == "IR WHOT");
        }

        private RectTransform BuildScreen(RectTransform host, float height)
        {
            console = AvConsole.Build(host, MfdSlots.Drones, "DRONES", 1, AvTokens.PanelWidth, height);
            console.Tabs((AvIcon.Drone, "DRONES"));
            AvFlow page = console.Page(0);
            rosterSection = page.Section(AvIcon.ListDetails, "AIRBORNE");
            roster = page.Add(new AvList(page.Content, console.Ticker, 4, BindDrone) { RowClicked = Link });
            selectionHint = page.Add(new AvNote(page.Content));
            flight = page.Buttons(
                new AvControl.Spec("LOCATE", Locate, AvButtonStyle.Default, AvIcon.Focus2),
                new AvControl.Spec("TAKE CONTROL", Pilot, AvButtonStyle.Primary, AvIcon.Pilot),
                new AvControl.Spec("SHARE WITH TEAM", Share, AvButtonStyle.Toggle, AvIcon.Link));
            locate = flight.Controls[0];
            pilot = flight.Controls[1];
            share = flight.Controls[2];
            locate.Help = "Center the tactical map on the selected drone.";
            share.Help = "Allow teammates to pilot this drone. Press again to make it private.";

            feedSection = page.Section(AvIcon.Camera, "SENSOR");
            feed = page.Add(new FeedPicture(page.Content));
            optics = page.Buttons(
                new AvControl.Spec("VIEW", () => { if (ModuleServices.TryGet(out IDroneCommand d)) d.ToggleLook(); }, AvButtonStyle.Default, AvIcon.Eye),
                new AvControl.Spec("ZOOM", () => { if (ModuleServices.TryGet(out IDroneCommand d)) d.CycleZoom(); }, AvButtonStyle.Default, AvIcon.Maximize),
                new AvControl.Spec("HUD FEED", () => { if (ModuleServices.TryGet(out IDroneCommand d)) d.ToggleFeed(); }, AvButtonStyle.Toggle, AvIcon.Camera));
            look = optics.Controls[0];
            zoom = optics.Controls[1];
            hud = optics.Controls[2];
            look.Help = "Switch the sensor view.";
            zoom.Help = "Cycle the sensor magnification.";
            hud.Help = "Show or hide the selected drone's picture on the flight HUD.";

            console.Ticker.Add(0, AvTickRate.Fast, Refresh);
            console.Finish();
            Refresh();
            return null;
        }

        private void Link(int index)
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones) || !rosterTracks.TryGetValue(index, out Unit track)) return;
            index = IndexOf(drones, track);
            if (index < 0) return;
            drones.Link(index);
            Refresh();
        }

        private static void Locate()
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones) || drones.LinkedIndex < 0) return;
            Unit track = drones.Track(drones.LinkedIndex);
            if (MapFocus.ToAirTrack(track, true)) MfdMapInteractions.Echo(drones.Name(drones.LinkedIndex));
        }

        private void Pilot()
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones)) return;
            if (drones.Piloting) drones.StopPiloting();
            else if (drones.CanPilot(drones.LinkedIndex)) drones.Pilot(drones.LinkedIndex);
            Refresh();
        }

        private void Share()
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones) || !drones.Owns(drones.LinkedIndex)) return;
            drones.ToggleShare(drones.LinkedIndex);
            Refresh();
        }

        private void BindDrone(int index, AvRow row)
        {
            if (!ModuleServices.TryGet(out IDroneCommand drones)) return;
            rosterTracks[index] = drones.Track(index);
            bool selected = index == drones.LinkedIndex;
            string access = drones.Owns(index) ? (drones.IsShared(index) ? "SHARED" : "PRIVATE") : "TEAM";
            row.Set(drones.Name(index), drones.State(index), access, selected ? AvState.Ready : AvState.Info);
            row.Armed = selected;
            row.Help = "Select this drone for the sensor and flight controls.";
        }

        private void Refresh()
        {
            if (console == null || !ModuleServices.TryGet(out IDroneCommand drones)) return;
            int selected = drones.LinkedIndex;
            bool linked = selected >= 0;
            bool own = linked && drones.Owns(selected);
            bool shared = linked && drones.IsShared(selected);
            bool piloting = drones.Piloting;
            roster.SetShown(drones.Linkable > 0);
            selectionHint.SetShown(!linked);
            if (!linked) selectionHint.Set(drones.Linkable > 0
                ? "Select a drone to view its sensor and controls."
                : "Your airborne drones and any shared by teammates appear here.");
            flight.SetShown(linked || piloting);
            feedSection.SetShown(linked);
            feed.SetShown(linked);
            optics.SetShown(linked);
            locate.Interactable = linked && drones.Track(selected) != null;
            pilot.Interactable = piloting || (linked && drones.CanPilot(selected));
            pilot.Label = piloting ? "RETURN TO AIRCRAFT" : "TAKE CONTROL";
            string pilotHelp = piloting ? "Give the drone back to its automatic flight logic."
                : "Fly the selected drone with your normal aircraft controls.";
            if (pilot.Help != pilotHelp) pilot.Help = pilotHelp;
            share.Interactable = own;
            share.Latched = shared;
            share.Label = own && shared ? "MAKE PRIVATE" : "SHARE WITH TEAM";
            look.Interactable = zoom.Interactable = hud.Interactable = linked;
            look.Label = "VIEW " + drones.LookWord;
            zoom.Label = "ZOOM " + drones.ZoomWord;
            hud.Latched = drones.FeedShown;
            rosterSection.SetCaption(drones.Linkable == 0 ? "NO DRONES" : AvNum.Fixed(drones.Linkable, 0) + " AVAILABLE");
            rosterTracks.Clear();
            roster.SetCount(drones.Linkable);
            if (selected != lastSelection && linked) roster.Reveal(selected);
            lastSelection = selected;
            feedSection.SetCaption(linked ? drones.Name(selected) : "NO SELECTION");
            console.Footer.Set(piloting ? "REMOTE FLIGHT ACTIVE"
                : linked ? drones.State(selected)
                : "");
        }

        private sealed class FeedPicture : AvPart
        {
            private readonly RawImage picture;
            private readonly CamFeedEffect effect = new CamFeedEffect(CamFeedMode.DroneEo);
            private readonly TMP_Text idle;

            internal FeedPicture(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "DroneFeed");
                AvLay.Fill(AvLay.Solid(Rect, "Back", Color.black).rectTransform);
                picture = new GameObject("Picture", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<RawImage>();
                picture.rectTransform.SetParent(Rect, false);
                picture.raycastTarget = false;
                AvLay.Fill(picture.rectTransform);
                effect.Bind(picture);
                idle = AvText.Make(Rect, "Idle", AvTextRole.DataSmall, "NO SIGNAL", TextAlignmentOptions.Center);
                AvLay.Fill(idle.rectTransform);
                Restyle();
            }

            internal void Show(Texture texture, bool ir)
            {
                effect.Bind(picture);
                effect.SetMode(ir ? CamFeedMode.DroneIr : CamFeedMode.DroneEo);
                effect.SetSignal(texture != null ? 1f : 0f);
                if (picture.texture != texture) picture.texture = texture;
                picture.enabled = texture != null;
                idle.gameObject.SetActive(texture == null);
            }

            internal void Dispose() => effect.Dispose();

            public override float Measure(float width) => Mathf.Round(width * 9f / 16f);

            public override void Restyle() => idle.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
        }
    }
}
