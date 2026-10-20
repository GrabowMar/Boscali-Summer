using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>One linkable drone as the drone views show it; refreshed at 5 Hz by <see cref="DroneLinkService"/>.</summary>
    internal sealed class DroneView
    {
        public Missile Missile;
        public VanguardRole Role;
        public string Callsign = "", Type = "", Job = "", State = "";
        public DroneMode Mode;
        /// <summary>The WPT point (global), valid while <see cref="Mode"/> is WPT.</summary>
        public Vector3 Point;
        /// <summary>Global position.</summary>
        public Vector3 Position;
        public float RangeM, BearingDeg, HeadingDeg, AltM, SpeedMs, Quality, Age;
        public float SampledAt = -1f;
    }

    /// <summary>
    /// The pilot's end of the VANGUARD drone link, client-local: the local aircraft's linkable drones (<see cref="DroneRoster"/>), the ONE
    /// linked drone, a mirror of the orders sent for them (the host owns the truth and applies the same link rule, as DESIGNATE does), and
    /// the one feed camera on the linked drone that the UAV terminal, the UAV MFD and the HUD feed card all show. Also the radial menu's
    /// <see cref="IDroneCommand"/>.
    /// </summary>
    [DefaultExecutionOrder(100)] // the lens is placed in LateUpdate, after the floating-origin shift (CameraStateManager, order 2)
    internal sealed class DroneLinkService : MonoBehaviour, ISceneService, IDroneCommand
    {
        private const float RefreshInterval = 0.2f, WantedFor = 0.5f, LostHold = 1.6f, LensPitchDeg = 10f;
        private static readonly float[] ZoomFov = { 50f, 20f, 8f };
        private static readonly string[] ZoomWords = { "1X", "3X", "7X" };

        private VanguardNet net;
        private readonly List<DroneRoster.Entry> entries = new List<DroneRoster.Entry>(16);
        private readonly List<DroneView> views = new List<DroneView>(16);
        private readonly Dictionary<Missile, SteerOrders.Order> mirror = new Dictionary<Missile, SteerOrders.Order>();
        private readonly List<Missile> stale = new List<Missile>();
        private readonly FeedCamera feed = new FeedCamera("BoscaliDroneFeed", 480, 270, FeedLook.Eo, pointFilter: false) { Fps = 20f, FieldOfView = 50f };
        private Missile linked;
        private string linkedName = "";
        private Aircraft owner;
        private float nextRefresh, wantedUntil, switchedAt = -9f, lostAt = -9f;
        private bool feedShown, feedLive;
        private int zoom;
        private DroneTerminalWindow terminal;
        private Texture testFeed;

#if !PUBLIC_RELEASE
        /// <summary>Offline render seam (DroneLinkUnityCheck): a fixed roster, link, own position and picture; no game, no refresh.</summary>
        internal void Fixture(List<DroneView> drones, int linkedIndex, Texture picture, Vector3 self, float heading)
        {
            views.Clear();
            views.AddRange(drones);
            Count = drones.Count;
            LinkedIndex = linkedIndex;
            linked = linkedIndex >= 0 ? drones[linkedIndex].Missile : null;
            testFeed = picture;
            SelfPosition = self;
            SelfHeading = heading;
            nextRefresh = float.MaxValue;
            Log.Clear();
            Note("LINKED " + (linkedIndex >= 0 ? drones[linkedIndex].Callsign : "—"));
        }
#endif

        public void Configure(VanguardNet network) => net = network;

        // ---- what the views read -------------------------------------------------------------------------------------------

        public int Count { get; private set; }
        public DroneView this[int index] => views[index];
        public int LinkedIndex { get; private set; } = -1;
        public DroneView Linked => LinkedIndex >= 0 ? views[LinkedIndex] : null;
        /// <summary>The feed picture of the linked drone (live, or the frozen last frame while <see cref="Lost"/>); null with none.</summary>
        public Texture Feed => testFeed != null ? testFeed : feedLive || Lost ? feed.Texture : null;
        /// <summary>The linked drone just went down: its last frame is held under SIGNAL LOST.</summary>
        public bool Lost => Time.unscaledTime - lostAt < LostHold;
        public string LostName { get; private set; } = "";
        /// <summary>Seconds since the link moved to another drone (the views' static burst).</summary>
        public float SinceSwitch => Time.unscaledTime - switchedAt;
        public FeedLook Look => feed.Look;
        public string ZoomWord => ZoomWords[zoom];
        public bool TerminalOpen => terminal != null && terminal.IsOpen;
        public bool HasAircraft => owner != null || testFeed != null;
        /// <summary>The local aircraft's global position and true heading at the last refresh (the plot's centre).</summary>
        public Vector3 SelfPosition { get; private set; }
        public float SelfHeading { get; private set; }
        /// <summary>Recent link events, newest last ("19:42 REMORA 2 · HAND"), at most <see cref="LogLines"/>.</summary>
        public readonly List<string> Log = new List<string>(LogLines + 1);
        public const int LogLines = 12;

        private void Note(string text)
        {
            Log.Add(DroneLink.Clock(Time.timeSinceLevelLoad) + "  " + text);
            if (Log.Count > LogLines) Log.RemoveAt(0);
        }

        /// <summary>A view shows the feed this frame: the camera runs while someone asks.</summary>
        public void Want() => wantedUntil = Time.unscaledTime + WantedFor;

        // ---- IDroneCommand -----------------------------------------------------------------------------------------------------

        public bool Striking => net != null && net.Striking;
        public bool CanStrike => net != null && net.CanStrike;

        public int Ready
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Count; i++) if (views[i].Role == VanguardRole.Drone) n++;
                return n;
            }
        }

        public void Strike() => net?.Strike();
        public void Screen() => net?.Screen();
        public int Linkable => Count;
        public string Name(int index) => index >= 0 && index < Count ? views[index].Callsign : "";
        public string State(int index) => index >= 0 && index < Count ? views[index].State : "";
        public bool HandFlying => Linked != null && Linked.Mode == DroneMode.Hand;
        public bool FeedShown => feedShown;

        public void Link(int index)
        {
            if (index < 0 || index >= Count) return;
            if (views[index].Missile != linked)
            {
                linked = views[index].Missile;
                linkedName = views[index].Callsign;
                switchedAt = Time.unscaledTime;
                Note("LINKED " + linkedName);
                AvUiSound.Play(AvUiCue.Navigate);
            }
            LinkedIndex = index;
            lostAt = -9f;
        }

        public void Cycle(int step)
        {
            bool hand = HandFlying;
            Link(DroneLink.Step(Count, LinkedIndex, step));
            if (hand && Linked != null && Linked.Mode != DroneMode.Hand) Order(Linked, DroneMode.Hand, Vector3.zero); // the stick follows the link
        }

        public void ToggleHand()
        {
            if (Linked == null) Link(0);
            DroneView v = Linked;
            if (v != null) Order(v, v.Mode == DroneMode.Hand ? DroneMode.Auto : DroneMode.Hand, Vector3.zero);
        }

        public void ToggleFeed()
        {
            feedShown = !feedShown;
            if (feedShown && Linked == null) Link(0);
            AvUiSound.Play(feedShown ? AvUiCue.Engage : AvUiCue.Release);
        }

        public void OpenTerminal()
        {
            if (terminal == null) terminal = DroneTerminalWindow.Create(transform, this);
            if (Linked == null) Link(0);
            terminal?.Open();
        }

        // ---- terminal-only orders --------------------------------------------------------------------------------------------

        /// <summary>WPT for the linked drone at a ground point (x/z); it circles there at its present height.</summary>
        public void SendTo(Vector3 ground)
        {
            DroneView v = Linked;
            if (v == null) return;
            Order(v, DroneMode.Waypoint, new Vector3(ground.x, Mathf.Max(v.AltM, 300f), ground.z));
        }

        public void Release()
        {
            if (Linked != null && Linked.Mode != DroneMode.Auto) Order(Linked, DroneMode.Auto, Vector3.zero);
        }

        public void ToggleLook()
        {
            feed.Look = feed.Look == FeedLook.Eo ? FeedLook.WhiteHot : FeedLook.Eo;
            switchedAt = Time.unscaledTime - 0.35f; // a short tear, not a full re-acquire
            AvUiSound.Play(AvUiCue.Press);
        }

        public void CycleZoom()
        {
            zoom = (zoom + 1) % ZoomFov.Length;
            AvUiSound.Play(AvUiCue.Press);
        }

        public string LookWord => feed.Look == FeedLook.WhiteHot ? "IR WHOT" : "EO DAY";

        private void Order(DroneView v, DroneMode mode, Vector3 point)
        {
            if (mode == DroneMode.Hand)
            {
                stale.Clear();
                foreach (KeyValuePair<Missile, SteerOrders.Order> pair in mirror) if (pair.Value.Mode == DroneMode.Hand) stale.Add(pair.Key);
                foreach (Missile m in stale) mirror.Remove(m);
            }
            if (mode == DroneMode.Auto) mirror.Remove(v.Missile);
            else mirror[v.Missile] = new SteerOrders.Order { Mode = mode, Point = new GlobalPosition(point) };
            net?.SendLink(v.Missile, mode, new GlobalPosition(point));
            Note(v.Callsign + " · " + (mode == DroneMode.Hand ? "HAND CONTROL" : mode == DroneMode.Auto ? "BACK ON TASK" : "WPT SET"));
            AvUiSound.Play(mode == DroneMode.Hand ? AvUiCue.Engage : mode == DroneMode.Auto ? AvUiCue.Release : AvUiCue.Confirm);
            Refresh();
        }

        // ---- lifecycle -------------------------------------------------------------------------------------------------------

        public void ResetForScene()
        {
            if (terminal != null) Destroy(terminal.gameObject);
            terminal = null;
            feed.Release();
            entries.Clear();
            mirror.Clear();
            linked = null;
            owner = null;
            Count = 0;
            LinkedIndex = -1;
            feedShown = feedLive = false;
            lostAt = switchedAt = -9f;
            Log.Clear();
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
            feedLive = v != null && v.Missile != null && !v.Missile.disabled && (Time.unscaledTime < wantedUntil || feedShown || HandFlying);
            if (!feedLive) { feed.Idle(); return; }
            Transform t = v.Missile.transform;
            Vector3 flat = new Vector3(t.forward.x, 0f, t.forward.z);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            // A stabilised sensor ball under the nose: the drone's heading, a little nose-down, never its roll.
            Quaternion lens = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(LensPitchDeg, 0f, 0f);
            feed.FieldOfView = ZoomFov[zoom];
            feedLive = feed.Shoot(t.position + t.forward * (v.Missile.maxRadius + 0.6f) - t.up * 0.3f, lens, Time.unscaledTime);
        }

        private void Refresh()
        {
            Aircraft me = GameManager.GetLocalAircraft(out Aircraft local) && local != null && !local.disabled ? local : null;
            if (me != owner)
            {
                owner = me;          // a respawn starts clean
                linked = null;
                mirror.Clear();
            }
            DroneRoster.Mine(me, entries);
            while (views.Count < entries.Count) views.Add(new DroneView());
            Count = entries.Count;
            stale.Clear();
            foreach (Missile m in mirror.Keys) if (m == null || m.disabled) stale.Add(m);
            foreach (Missile m in stale) mirror.Remove(m);

            int found = -1;
            Vector3 self = me != null ? me.GlobalPosition().AsVector3() : Vector3.zero;
            SelfPosition = self;
            SelfHeading = me != null ? DroneLink.Bearing(Vector3.zero, me.transform.forward) : 0f;
            float now = Time.unscaledTime;
            for (int i = 0; i < Count; i++)
            {
                DroneRoster.Entry e = entries[i];
                DroneView v = views[i];
                Vector3 pos = e.Missile.GlobalPosition().AsVector3();
                if (v.Missile != e.Missile)
                {
                    v.Missile = e.Missile;
                    v.Role = e.Role;
                    v.Callsign = e.Callsign;
                    v.Type = DroneLink.Type(e.Role);
                    v.Job = DroneLink.Job(e.Role);
                    v.SampledAt = -1f;
                    v.SpeedMs = 0f;
                }
                if (v.SampledAt > 0f && now - v.SampledAt > 0.05f)
                    v.SpeedMs = Mathf.Lerp(v.SpeedMs, Vector3.Distance(pos, v.Position) / (now - v.SampledAt), 0.5f);
                v.Position = pos;
                v.SampledAt = now;
                v.AltM = pos.y;
                Vector3 fwd = e.Missile.transform.forward;
                v.HeadingDeg = DroneLink.Bearing(Vector3.zero, fwd);
                v.RangeM = me != null ? Vector3.Distance(self, pos) : 0f;
                v.BearingDeg = DroneLink.Bearing(self, pos);
                v.Quality = me != null ? DroneLink.Quality(v.RangeM) : 0f;
                v.Age = Time.time - e.SeenAt;
                if (mirror.TryGetValue(e.Missile, out SteerOrders.Order order) && !DroneLink.InRange(v.RangeM)) { mirror.Remove(e.Missile); order = default; } // the host drops it too
                v.Mode = mirror.TryGetValue(e.Missile, out order) ? order.Mode : DroneMode.Auto;
                v.Point = order.Point.AsVector3();
                v.State = DroneLink.Word(v.Mode) + " · " + AvNum.Fixed(v.RangeM / 1000f, 1) + " KM";
                if (e.Missile == linked) found = i;
            }
            if (linked != null && found < 0)
            {
                // The linked drone is gone: hold its last frame under SIGNAL LOST, then link the next one.
                LostName = linkedName;
                Note("SIGNAL LOST " + linkedName);
                lostAt = now;
                linked = null;
            }
            LinkedIndex = found;
            if (linked == null && Count > 0 && !Lost && lostAt > 0f) { lostAt = -9f; Link(0); }
        }
    }
}
