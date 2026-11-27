using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NOAvionics;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    internal sealed class DroneView
    {
        public Missile Missile;
        public VanguardRole Role;
        public string Callsign = "", Job = "", State = "";
        public bool Occupied;
        public float RangeM, BearingDeg, HeadingDeg, AltM, SpeedMs, Quality, Age;
        public string FlightState => Occupied ? "PILOTED" : "AUTO";
    }

    /// <summary>The local roster selection and its shared sensor feed. The host owns sharing and the active pilot.</summary>
    [DefaultExecutionOrder(100)]
    internal sealed class DroneLinkService : MonoBehaviour, ISceneService, IDroneCommand
    {
        private const float RefreshInterval = 0.2f, WantedFor = 0.5f, LostHold = 1.6f, SlewDegPerSec = 120f;
        private static readonly float[] ZoomFov = { 50f, 20f, 8f };
        private static readonly string[] ZoomWords = { "1X", "3X", "7X" };

        private VanguardNet net;
        private readonly List<DroneRoster.Entry> entries = new List<DroneRoster.Entry>(16);
        private readonly List<DroneView> views = new List<DroneView>(16);
        private readonly FeedCamera feed = new FeedCamera("BoscaliDroneFeed", 384, 216, FeedLook.Seeker, pointFilter: false) { Fps = 15f, FieldOfView = 50f };
        private Missile linked, lensOn;
        private string linkedName = "";
        private float nextRefresh, wantedUntil, switchedAt = -9f, lostAt = -9f, nextSpi;
        private bool feedLive;
        private int zoom;
        private Quaternion lens = Quaternion.identity;
#if !PUBLIC_RELEASE
        private Texture fixtureFeed;

        internal void Fixture(List<DroneView> drones, int index, Texture texture)
        {
            views.Clear();
            views.AddRange(drones);
            Count = drones.Count;
            LinkedIndex = index;
            fixtureFeed = texture;
        }

        internal void DescribeFeed(Dictionary<string, object> data)
        {
            data["feedRequested"] = Time.unscaledTime < wantedUntil || FeedShown;
            data["feedTexture"] = feed.Texture != null;
            FxRtPool.Describe(data);
        }
#endif

        public void Configure(VanguardNet network) => net = network;

        public int Count { get; private set; }
        public int LinkedIndex { get; private set; } = -1;
        public DroneView Linked => LinkedIndex >= 0 ? views[LinkedIndex] : null;
        public Texture Feed
        {
            get
            {
#if !PUBLIC_RELEASE
                if (fixtureFeed != null) return fixtureFeed;
#endif
                return feedLive || Lost ? feed.Texture : null;
            }
        }
        public bool Lost => Time.unscaledTime - lostAt < LostHold;
        public string LostName { get; private set; } = "";
        public float SinceSwitch => Time.unscaledTime - switchedAt;
        public string ZoomWord => ZoomWords[zoom];
        public string LookWord => feed.Look == FeedLook.WhiteHot ? "IR WHOT" : "EO DAY";
        public bool FeedShown { get; private set; }
        public Unit Mark => net != null && Own != null && Linked != null && Linked.Missile != null && Linked.Missile.owner == Own
            ? net.Designated : null;
        public bool Tracking { get; private set; }
        public string LensWord { get; private set; } = "STARE";
        public float LensElevation { get; private set; } = -DroneLink.StareDeg;
        public float LensAzimuth { get; private set; }
        public float FieldOfView => ZoomFov[zoom];
        public bool HasSpi { get; private set; }
        public float SpiSlantM { get; private set; }
        public Vector3 SpiGlobal { get; private set; }
        public int FrameNumber => Mathf.FloorToInt(Time.unscaledTime * feed.Fps);
        public Aircraft Own { get; private set; }

        public bool Project(Vector3 world, out Vector2 viewport) => feed.Project(world, out viewport);

        public bool MarkInView(out Vector2 viewport, out string name, out float rangeM)
        {
            viewport = default;
            name = "";
            rangeM = 0f;
            Unit mark = Mark;
            DroneView v = Linked;
            if (mark == null || v == null || v.Missile == null || !Project(mark.transform.position, out viewport)) return false;
            name = mark.unitName;
            rangeM = Vector3.Distance(mark.transform.position, v.Missile.transform.position);
            return viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f;
        }

        public bool Stores(out RemoraView view)
        {
            view = default;
            DroneView v = Linked;
            return v != null && v.Role == VanguardRole.Drone && v.Missile != null &&
                RemoraBoard.Views.TryGetValue(v.Missile.persistentID.Id, out view);
        }

        public void Want() => wantedUntil = Time.unscaledTime + WantedFor;
        public Texture WantFeed() { Want(); return Feed; }

        public bool Striking => net != null && net.Striking;
        public bool CanStrike => net != null && net.CanStrike;
        public bool RamMode => net != null && net.RamMode;
        public int Ready
        {
            get
            {
                int count = 0;
                foreach (DroneRoster.Entry entry in entries)
                    if (Own != null && entry.Missile != null && entry.Missile.owner == Own && entry.Role == VanguardRole.Drone) count++;
                return count;
            }
        }

        public void Strike() { if (net != null) net.Strike(); }
        public void Screen() { if (net != null) net.Screen(); }
        public void ToggleRamMode() { if (net != null) net.SetRamMode(!RamMode); }

        public int Linkable => Count;
        public Unit Track(int index) => index >= 0 && index < Count ? views[index].Missile : null;
        public string Name(int index) => index >= 0 && index < Count ? views[index].Callsign : "";
        public string State(int index) => index >= 0 && index < Count ? views[index].State : "";
        private DroneRoster.Entry Entry(int index) => index >= 0 && index < entries.Count ? entries[index] : null;
        private static Player LocalPlayer() => GameManager.GetLocalPlayer(out Player player) ? player : null;

        public void Link(int index)
        {
            if (index < 0 || index >= Count || views[index].Missile == null) return;
            if (views[index].Missile != linked)
            {
                linked = views[index].Missile;
                linkedName = views[index].Callsign;
                switchedAt = Time.unscaledTime;
                feedLive = HasSpi = Tracking = false;
                AvUiSound.Play(AvUiCue.Navigate);
            }
            LinkedIndex = index;
            lostAt = -9f;
        }

        public bool Owns(int index) => Entry(index)?.Owner(LocalPlayer()) == true;
        public bool IsShared(int index) => Entry(index)?.Shared == true;
        public bool Piloting => DroneRoster.ControlledBy(LocalPlayer()) != null;

        public bool CanPilot(int index)
        {
            DroneRoster.Entry entry = Entry(index);
            Player player = LocalPlayer();
            return player != null && DronePilot.SupportsAircraft(player.Aircraft) && entry != null && entry.VisibleTo(player) && !entry.Occupied &&
                DroneRoster.ControlledBy(player) == null && DroneRoster.InRange(player, entry);
        }

        public void ToggleShare(int index)
        {
            DroneRoster.Entry entry = Entry(index);
            if (net != null && entry != null && entry.Owner(LocalPlayer()))
                net.SendDroneAction(entry.Missile, entry.Shared ? DroneAction.Private : DroneAction.Share);
        }

        public void Pilot(int index)
        {
            if (net == null || !CanPilot(index)) return;
            Link(index);
            net.SendDroneAction(entries[index].Missile, DroneAction.Pilot);
        }

        public void StopPiloting()
        {
            DroneRoster.Entry entry = DroneRoster.ControlledBy(LocalPlayer());
            if (entry == null) return;
            DronePilot pilot = DronePilot.Active;
            if (pilot != null && pilot.Current == entry.Missile) pilot.Release();
            else if (net != null) net.SendDroneAction(entry.Missile, DroneAction.Release);
        }

        public void ToggleFeed()
        {
            FeedShown = !FeedShown;
            if (FeedShown && Linked == null) Link(0);
            AvUiSound.Play(FeedShown ? AvUiCue.Engage : AvUiCue.Release);
        }

        public void ToggleLook()
        {
            feed.Look = feed.Look == FeedLook.WhiteHot ? FeedLook.Seeker : FeedLook.WhiteHot;
            switchedAt = Time.unscaledTime - 0.35f;
            AvUiSound.Play(AvUiCue.Press);
        }

        public void CycleZoom()
        {
            zoom = (zoom + 1) % ZoomFov.Length;
            AvUiSound.Play(AvUiCue.Press);
        }

        public void ResetForScene()
        {
            feed.Release();
            entries.Clear();
            views.Clear();
            linked = lensOn = null;
            Own = null;
            Count = 0;
            LinkedIndex = -1;
            linkedName = LostName = "";
            FeedShown = feedLive = HasSpi = Tracking = false;
            lostAt = switchedAt = -9f;
            nextRefresh = wantedUntil = nextSpi = 0f;
#if !PUBLIC_RELEASE
            fixtureFeed = null;
#endif
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void LateUpdate()
        {
            DroneView v = Linked;
            feedLive = v != null && v.Quality > 0f && v.Missile != null && !v.Missile.disabled &&
                (Time.unscaledTime < wantedUntil || FeedShown && !Piloting);
            if (!feedLive)
            {
                feed.Idle();
                HasSpi = Tracking = false;
                return;
            }
            Transform t = v.Missile.transform;
            Vector3 at = t.position + t.forward * (v.Missile.maxRadius + 0.6f) - t.up * 0.3f;
            Quaternion want = Aim(v, t, at);
            lens = v.Missile != lensOn ? want : Quaternion.RotateTowards(lens, want, SlewDegPerSec * Time.deltaTime);
            lensOn = v.Missile;
            Vector3 look = lens * Vector3.forward;
            LensElevation = Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;
            LensAzimuth = Mathf.DeltaAngle(DroneLink.Bearing(Vector3.zero, t.forward), DroneLink.Bearing(Vector3.zero, look));
            feed.FieldOfView = FieldOfView;
            feedLive = feed.Shoot(at, lens, Time.unscaledTime);
            if (Time.unscaledTime >= nextSpi) { nextSpi = Time.unscaledTime + RefreshInterval; Spi(at, look); }
        }

        private Quaternion Aim(DroneView v, Transform t, Vector3 at)
        {
            Vector3 flat = new Vector3(t.forward.x, 0f, t.forward.z);
            Quaternion ahead = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward);
            Unit mark = v.Occupied ? null : Mark;
            Vector3 to = mark != null ? mark.transform.position - at : Vector3.zero;
            Tracking = to.sqrMagnitude > 1f && to.sqrMagnitude < DroneLink.MaxRangeM * DroneLink.MaxRangeM;
            LensWord = Tracking ? "TRK" : v.Occupied ? "FWD" : "STARE";
            return Tracking ? Quaternion.LookRotation(to) : ahead * Quaternion.Euler(v.Occupied ? DroneLink.ForwardDeg : DroneLink.StareDeg, 0f, 0f);
        }

        private void Spi(Vector3 at, Vector3 look)
        {
            Unit mark = Tracking ? Mark : null;
            Vector3 hit;
            if (mark != null) hit = mark.transform.position;
            else if (Physics.Raycast(at, look, out RaycastHit ray, DroneLink.MaxRangeM, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) hit = ray.point;
            else
            {
                float sea = look.y < -0.01f ? (at.y - Datum.LocalSeaY) / -look.y : float.MaxValue;
                if (sea < 0f || sea >= DroneLink.MaxRangeM) { HasSpi = false; return; }
                hit = at + look * sea;
            }
            HasSpi = true;
            SpiSlantM = Vector3.Distance(at, hit);
            SpiGlobal = hit.ToGlobalPosition().AsVector3();
        }

        private void Refresh()
        {
            Player player = LocalPlayer();
            Own = player != null && player.Aircraft != null && !player.Aircraft.disabled ? player.Aircraft : null;
            DroneRoster.Visible(player, entries);
            while (views.Count < entries.Count) views.Add(new DroneView());
            Count = entries.Count;

            int found = -1;
            Vector3 self = Own != null ? Own.GlobalPosition().AsVector3() : Vector3.zero;
            float linkScale = WatchtowerPod.LinkScale(Own);
            for (int i = 0; i < Count; i++)
            {
                DroneRoster.Entry entry = entries[i];
                Missile missile = entry.Missile;
                DroneView v = views[i];
                Vector3 pos = missile.GlobalPosition().AsVector3();
                v.Missile = missile;
                v.Role = entry.Role;
                v.Callsign = entry.Callsign;
                v.Job = DroneLink.Job(entry.Role);
                v.SpeedMs = missile.speed;
                v.AltM = pos.y;
                v.HeadingDeg = DroneLink.Bearing(Vector3.zero, missile.transform.forward);
                v.RangeM = Own != null ? Vector3.Distance(self, pos) : 0f;
                v.BearingDeg = DroneLink.Bearing(self, pos);
                v.Quality = Own != null ? DroneLink.Quality(v.RangeM, linkScale) : 0f;
                v.Age = Time.time - entry.SeenAt;
                v.Occupied = entry.Occupied;
                string state = entry.ControlledBy(player) ? "YOU ARE FLYING" : v.FlightState;
                v.State = Own != null ? state + " · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM" : state;
                if (missile == linked) found = i;
            }
            if (LinkedIndex >= 0 && found < 0)
            {
                LostName = linkedName;
                lostAt = Time.unscaledTime;
                linked = null;
            }
            LinkedIndex = found;
            if (linked == null && Count > 0 && !Lost) Link(0);
        }
    }
}
